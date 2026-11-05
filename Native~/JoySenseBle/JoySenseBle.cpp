// JoySenseBle: the Bluetooth LE transport for the Nintendo Switch 2 controllers, as a plain C API.
//
// These controllers do not speak Bluetooth HID. Over the air they are a plain BLE peripheral with a vendor
// GATT service, so Windows never enumerates them as a gamepad and Unity's HID backend cannot see them at all.
// Everything here talks to that GATT service directly: scan for the advertisement, connect, subscribe to the
// input characteristic, and write commands and vibration data to their own characteristics.
//
// Native because the GATT API is WinRT, which Unity's Mono cannot call. Everything else -- which commands to
// send, what the reports mean, when to connect -- stays in C#. Ported from JoyShockLibrary4Unreal's
// Switch2Bluetooth.cpp; the hardware facts in its comments come along.
//
// Nothing here calls back into managed code. Unity reloads its scripting domain on every recompile while this
// DLL stays loaded, and a callback into a domain that has gone is a crash. Instead managed code polls: for
// discovered controllers, for input reports, and for log lines. The same reason makes connections outlive a
// domain reload: they are owned here and named by integer handles, which the new domain looks up again.

#define NOMINMAX
#include <windows.h>
#include <objbase.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Devices.Bluetooth.h>
#include <winrt/Windows.Devices.Enumeration.h>
#include <winrt/Windows.Devices.Bluetooth.Advertisement.h>
#include <winrt/Windows.Devices.Bluetooth.GenericAttributeProfile.h>
#include <winrt/Windows.Storage.Streams.h>

#include <algorithm>
#include <atomic>
#include <cstdarg>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <deque>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#define JSBLE_API extern "C" __declspec(dllexport)

using namespace winrt::Windows::Devices::Bluetooth;
using namespace winrt::Windows::Devices::Bluetooth::Advertisement;
using namespace winrt::Windows::Devices::Bluetooth::GenericAttributeProfile;
using namespace winrt::Windows::Storage::Streams;

using Clock = std::chrono::steady_clock;

// One controller seen advertising. Mirrored by a C# struct; keep the layout in step.
struct JsBleAdvertisement
{
    // The controller's own BLE address, and the only stable name it has before a connection exists.
    uint64_t address;
    // The host this controller is already bonded to, or 0 when it is advertising for a new one (the state it
    // enters when the SYNC button is held).
    uint64_t reconnectAddress;
    uint16_t vendorId;
    uint16_t productId;
    uint32_t reserved;
};

// The radio state of one connection, for diagnostics. Mirrored by a C# struct; keep the layout in step.
struct JsBleLinkInfo
{
    // In units of 1.25ms.
    int32_t connectionInterval;
    int32_t connectionLatency;
    // In units of 10ms.
    int32_t linkTimeout;
    // 0 unknown, 1 uncoded 1M, 2 uncoded 2M, 3 coded.
    int32_t transmitPhy;
    int32_t receivePhy;
};

namespace
{
    // Nintendo's company identifier, as it appears in the manufacturer data of a controller's advertisement.
    constexpr uint16_t NintendoManufacturerId = 0x0553;

    // The controllers' vendor GATT service and its characteristics. Every Switch 2 controller exposes the
    // same set; only the vibration characteristic differs between the Pro Controller and each Joy-Con,
    // because a Joy-Con pair is two peripherals driving one actuator each.
    const winrt::guid ServiceUuid          { 0xab7de9be, 0x89fe, 0x49ad, { 0x82, 0x8f, 0x11, 0x8f, 0x09, 0xdf, 0x7f, 0xd0 } };
    const winrt::guid InputReportUuid      { 0xab7de9be, 0x89fe, 0x49ad, { 0x82, 0x8f, 0x11, 0x8f, 0x09, 0xdf, 0x7f, 0xd2 } };
    const winrt::guid CommandWriteUuid     { 0x649d4ac9, 0x8eb7, 0x4e6c, { 0xaf, 0x44, 0x1e, 0xa5, 0x4f, 0xe5, 0xf0, 0x05 } };
    const winrt::guid CommandResponseUuid  { 0xc765a961, 0xd9d8, 0x4d36, { 0xa2, 0x0a, 0x53, 0x15, 0xb1, 0x11, 0x83, 0x6a } };
    const winrt::guid VibrationProUuid     { 0xcc483f51, 0x9258, 0x427d, { 0xa9, 0x39, 0x63, 0x0c, 0x31, 0xf7, 0x2b, 0x05 } };
    const winrt::guid VibrationJoyConLUuid { 0x289326cb, 0xa471, 0x485d, { 0xa8, 0xf4, 0x24, 0x0c, 0x14, 0xf1, 0x82, 0x41 } };
    const winrt::guid VibrationJoyConRUuid { 0xfa19b0fb, 0xcd1f, 0x46a7, { 0x84, 0xa1, 0xbb, 0xb0, 0x9e, 0x00, 0xc1, 0x49 } };

    // Switch 2 product ids as they appear in the advertisement, which is the only place they can be read
    // before a connection exists.
    bool IsSwitch2ProductId(uint16_t productId)
    {
        return productId == 0x2069 || productId == 0x2067 || productId == 0x2066;
    }

    // ---- Log lines, handed to managed code when it next polls. ----

    constexpr size_t MaxQueuedLogLines = 64;
    std::mutex g_LogLock;
    std::deque<std::pair<int, std::wstring>> g_Log;

    enum LogLevel { LogInfo = 0, LogWarning = 1 };

    void Log(int level, const wchar_t* format, ...)
    {
        wchar_t buffer[512];
        va_list args;
        va_start(args, format);
        _vsnwprintf_s(buffer, _countof(buffer), _TRUNCATE, format, args);
        va_end(args);

        std::lock_guard<std::mutex> lock(g_LogLock);
        if (g_Log.size() >= MaxQueuedLogLines)
        {
            g_Log.pop_front();
        }
        g_Log.emplace_back(level, buffer);
    }

    // C++/WinRT's blocking waits are only safe on the multi-threaded apartment: on a single-threaded one the
    // completion may need the very thread that is blocked waiting for it, and the wait never ends (seen with a
    // PowerShell caller, whose threads are STA). Callers are managed threads whose apartment this DLL does not
    // control, so every export that waits on WinRT runs its work here: in place on an MTA thread, otherwise on
    // a short-lived thread of its own in the MTA. That thread exits without uninitialising COM, which keeps
    // the MTA alive for the watcher and the connections created on it.
    template <typename Work>
    auto RunOnMta(Work&& work) -> decltype(work())
    {
        APTTYPE apartment{};
        APTTYPEQUALIFIER qualifier{};
        if (SUCCEEDED(CoGetApartmentType(&apartment, &qualifier)) && apartment == APTTYPE_MTA)
        {
            return work();
        }

        decltype(work()) result{};
        std::thread([&]
        {
            try
            {
                winrt::init_apartment(winrt::apartment_type::multi_threaded);
            }
            catch (...)
            {
            }
            result = work();
        }).join();
        return result;
    }

    // Waits for a WinRT operation, but not forever: a radio that stops answering mid-exchange must cost a failed
    // call, not a thread blocked for the rest of the session. A timed-out operation is cancelled and reported as
    // an error, which every caller already handles.
    template <typename Operation>
    auto Await(const Operation& operation, uint32_t seconds = 10)
    {
        const auto status = operation.wait_for(std::chrono::seconds(seconds));
        if (status == winrt::Windows::Foundation::AsyncStatus::Started)
        {
            operation.Cancel();
            throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_TIMEOUT), L"the operation timed out");
        }
        return operation.GetResults();
    }

    std::vector<uint8_t> BufferToVector(const IBuffer& buffer)
    {
        std::vector<uint8_t> result;
        if (buffer == nullptr || buffer.Length() == 0)
        {
            return result;
        }
        result.resize(buffer.Length());
        DataReader reader = DataReader::FromBuffer(buffer);
        reader.ReadBytes(winrt::array_view<uint8_t>(result.data(), result.data() + result.size()));
        return result;
    }

    IBuffer ToBuffer(const uint8_t* data, int32_t length)
    {
        DataWriter writer;
        writer.WriteBytes(winrt::array_view<const uint8_t>(data, data + length));
        return writer.DetachBuffer();
    }
}

// One live GATT connection. Input reports and command responses arrive on WinRT's own threads, so both are
// handed to the reading thread through a queue guarded by a lock rather than being acted on in place.
struct Connection
{
    int32_t handle = 0;
    uint64_t address = 0;
    uint16_t productId = 0;

    BluetoothLEDevice device{ nullptr };
    // The session is what actually holds the radio link up. Without one WinRT is free to drop the connection
    // the moment nothing is being read, and the controller goes quiet mid-game.
    GattSession session{ nullptr };
    // Held so it can be closed: an open service stays open after its last reference here goes, and a service
    // left open by a failed attempt answers every later one, in this process or another, with a sharing
    // violation.
    GattDeviceService service{ nullptr };
    // The connection-interval request, which is only in force while this object is alive -- releasing it is
    // how the request is withdrawn, so it is held for the life of the connection. See Connect.
    BluetoothLEPreferredConnectionParametersRequest connectionParametersRequest{ nullptr };
    GattCharacteristic inputCharacteristic{ nullptr };
    GattCharacteristic commandCharacteristic{ nullptr };
    GattCharacteristic commandResponseCharacteristic{ nullptr };
    GattCharacteristic vibrationCharacteristic{ nullptr };

    winrt::event_token inputToken{};
    winrt::event_token responseToken{};
    winrt::event_token connectionStatusToken{};

    std::atomic<bool> connected{ true };

    // Input reports. Bounded: a reader that stops polling must not grow it without limit, and an old report
    // is worth nothing next to a new one, so the oldest is dropped.
    static constexpr size_t MaxQueuedInputReports = 32;
    std::mutex inputLock;
    std::condition_variable inputSignal;
    std::deque<std::vector<uint8_t>> inputQueue;

    // Command responses. A queue rather than a single slot, because "the notification that arrived" and "the
    // answer to what was asked" are not the same thing: a memory read is answered with an acknowledgement and
    // then the data, and which of the two lands first depends on thread scheduling against a controller
    // streaming input every 7.5ms. A late answer to a command that already timed out would otherwise poison
    // the next command the same way.
    static constexpr size_t MaxQueuedResponses = 8;
    std::mutex responseLock;
    std::condition_variable responseSignal;
    std::deque<std::vector<uint8_t>> responseQueue;

    // Commands are strictly serialised; see JsBle_SendCommand.
    std::mutex commandLock;

    void MarkDisconnected()
    {
        connected = false;
        // Wake anyone blocked in a read or waiting on a command, so a dropped link surfaces immediately
        // instead of at the end of their timeout.
        {
            std::lock_guard<std::mutex> lock(inputLock);
        }
        inputSignal.notify_all();
        {
            std::lock_guard<std::mutex> lock(responseLock);
        }
        responseSignal.notify_all();
    }
};

namespace
{
    using ConnectionPtr = std::shared_ptr<Connection>;

    // Scanning state. The watcher raises its callback on a WinRT thread, so discovered controllers are queued
    // here and picked up when managed code next drains them.
    std::mutex g_ScanLock;
    BluetoothLEAdvertisementWatcher g_Watcher{ nullptr };
    std::vector<JsBleAdvertisement> g_Discovered;
    // When each address may be offered again, so a controller advertising several times a second is not
    // offered over and over. A controller that has been taken is suppressed until it is disconnected; one
    // whose connection attempt failed is suppressed briefly, so it is retried rather than hammered.
    constexpr auto SuppressAfterFailure = std::chrono::seconds(3);
    std::map<uint64_t, Clock::time_point> g_SuppressedUntil;

    // Connections are shared-owned and handed out as integer handles. A handle's holder (a reading thread)
    // may be blocked inside a read while a different thread -- shutdown, or the controller being turned off --
    // decides the connection is over. Every entry point resolves the handle back to a reference and holds it
    // for the duration of the call, so a connection cannot be freed under a caller still inside it; a call on
    // a handle that has gone is a no-op.
    std::mutex g_ConnectionLock;
    std::map<int32_t, ConnectionPtr> g_Connections;
    int32_t g_NextHandle = 1;

    ConnectionPtr Pin(int32_t handle)
    {
        std::lock_guard<std::mutex> lock(g_ConnectionLock);
        auto found = g_Connections.find(handle);
        return found != g_Connections.end() ? found->second : nullptr;
    }

    void OnAdvertisementReceived(const BluetoothLEAdvertisementReceivedEventArgs& args)
    {
        // Nintendo packs vendor, product and the bonded host into its manufacturer data. A device with none of
        // it is some other BLE device that happens to be in range.
        for (const BluetoothLEManufacturerData& section : args.Advertisement().ManufacturerData())
        {
            if (section.CompanyId() != NintendoManufacturerId)
            {
                continue;
            }

            const std::vector<uint8_t> data = BufferToVector(section.Data());
            if (data.size() < 7)
            {
                continue;
            }

            JsBleAdvertisement found{};
            found.address = args.BluetoothAddress();
            found.vendorId = static_cast<uint16_t>(data[3] | (data[4] << 8));
            found.productId = static_cast<uint16_t>(data[5] | (data[6] << 8));
            if (data.size() >= 16)
            {
                uint64_t mac = 0;
                for (int index = 0; index < 6; index++)
                {
                    mac |= static_cast<uint64_t>(data[10 + index]) << (index * 8);
                }
                found.reconnectAddress = mac;
            }

            if (!IsSwitch2ProductId(found.productId))
            {
                continue;
            }

            {
                std::lock_guard<std::mutex> lock(g_ScanLock);
                auto suppressed = g_SuppressedUntil.find(found.address);
                if (suppressed != g_SuppressedUntil.end() && Clock::now() < suppressed->second)
                {
                    return;
                }
                g_SuppressedUntil[found.address] = Clock::time_point::max();
                g_Discovered.push_back(found);
            }

            Log(LogInfo, L"Switch 2 controller %012llX is advertising (product %04X, paired to %012llX).",
                found.address, found.productId, found.reconnectAddress);
            return;
        }
    }

    void Teardown(const ConnectionPtr& connection)
    {
        {
            // Offer this controller again the next time it advertises: it has just become available.
            std::lock_guard<std::mutex> lock(g_ScanLock);
            g_SuppressedUntil.erase(connection->address);
        }

        connection->MarkDisconnected();

        try
        {
            // Handlers are removed first so no new notification is delivered. One already running keeps the
            // connection alive through its weak reference, which is what makes this safe without waiting.
            if (connection->inputCharacteristic != nullptr && connection->inputToken)
            {
                connection->inputCharacteristic.ValueChanged(connection->inputToken);
            }
            if (connection->commandResponseCharacteristic != nullptr && connection->responseToken)
            {
                connection->commandResponseCharacteristic.ValueChanged(connection->responseToken);
            }
            if (connection->device != nullptr && connection->connectionStatusToken)
            {
                connection->device.ConnectionStatusChanged(connection->connectionStatusToken);
            }
            if (connection->connectionParametersRequest != nullptr)
            {
                // Withdraws the short-interval request, so the radio is not left holding bandwidth for a
                // controller that has gone.
                connection->connectionParametersRequest.Close();
                connection->connectionParametersRequest = nullptr;
            }
            if (connection->service != nullptr)
            {
                connection->service.Close();
            }
            if (connection->session != nullptr)
            {
                connection->session.MaintainConnection(false);
                connection->session.Close();
            }
            if (connection->device != nullptr)
            {
                connection->device.Close();
            }
        }
        catch (...)
        {
        }
    }
}

// False when the machine has no Bluetooth LE adapter. Blocks; call it from a worker thread.
static int32_t IsSupportedImpl()
{
    // Cached: this asks the OS for the radio, which blocks. A machine does not grow an adapter mid-session.
    static std::atomic<int8_t> cachedSupport{ -1 };
    const int8_t cached = cachedSupport.load();
    if (cached >= 0)
    {
        return cached;
    }
    bool supported = false;
    try
    {
        BluetoothAdapter adapter = Await(BluetoothAdapter::GetDefaultAsync());
        supported = adapter != nullptr && adapter.IsLowEnergySupported();
    }
    catch (...)
    {
        supported = false;
    }

    if (!supported)
    {
        Log(LogInfo, L"No Bluetooth LE adapter available; Switch 2 controllers will only work over USB.");
    }
    cachedSupport.store(supported ? 1 : 0);
    return supported ? 1 : 0;
}

// The local adapter's address, which is what a controller is told to bond to. 0 when unavailable. Blocks.
static uint64_t GetHostAddressImpl()
{
    try
    {
        BluetoothAdapter adapter = Await(BluetoothAdapter::GetDefaultAsync());
        return adapter != nullptr ? adapter.BluetoothAddress() : 0;
    }
    catch (...)
    {
        return 0;
    }
}

// Scanning runs continuously in the background once started; discovered controllers accumulate until they are
// drained. Starting an already-running scan is a no-op.
static int32_t StartScanImpl()
{

    std::lock_guard<std::mutex> lock(g_ScanLock);
    if (g_Watcher != nullptr)
    {
        return 1;
    }

    try
    {
        g_Watcher = BluetoothLEAdvertisementWatcher();
        // Active scanning asks each advertiser for its scan response. The manufacturer data controllers are
        // identified by does not always fit in the initial advertisement.
        g_Watcher.ScanningMode(BluetoothLEScanningMode::Active);

        g_Watcher.Received([](const BluetoothLEAdvertisementWatcher&, const BluetoothLEAdvertisementReceivedEventArgs& args)
        {
            try
            {
                OnAdvertisementReceived(args);
            }
            catch (...)
            {
            }
        });

        g_Watcher.Stopped([](const BluetoothLEAdvertisementWatcher& sender, const BluetoothLEAdvertisementWatcherStoppedEventArgs&)
        {
            // The radio can abort a watcher on its own (adapter reset, radio switched off). Clearing the handle
            // here means the next StartScan builds a fresh one rather than reusing a dead watcher.
            std::lock_guard<std::mutex> scanLock(g_ScanLock);
            if (g_Watcher == sender)
            {
                g_Watcher = nullptr;
            }
        });

        g_Watcher.Start();
        Log(LogInfo, L"Scanning for Switch 2 controllers. Hold SYNC on a new one, or press a button on one already paired to this PC.");
        return 1;
    }
    catch (const winrt::hresult_error& error)
    {
        Log(LogWarning, L"Could not start the Bluetooth scan: %ls", error.message().c_str());
        g_Watcher = nullptr;
        return 0;
    }
    catch (...)
    {
        g_Watcher = nullptr;
        return 0;
    }
}

// Offers a controller again after the given delay, instead of keeping it back until a connection to it ends. For a
// controller the caller turned down: it advertises many times a second, and the caller wants to look at it again
// later -- after its owner holds SYNC, say -- without being handed it on every one of those.
JSBLE_API void JsBle_SuppressFor(uint64_t address, int32_t milliseconds)
{
    std::lock_guard<std::mutex> lock(g_ScanLock);
    g_SuppressedUntil[address] = Clock::now() + std::chrono::milliseconds(std::max(milliseconds, 0));
}

JSBLE_API void JsBle_StopScan()
{
    std::lock_guard<std::mutex> lock(g_ScanLock);
    if (g_Watcher == nullptr)
    {
        return;
    }
    try
    {
        g_Watcher.Stop();
    }
    catch (...)
    {
    }
    g_Watcher = nullptr;
    g_Discovered.clear();
    g_SuppressedUntil.clear();
}

// Copies out up to maxCount of the controllers seen since the last call, removing them from the pending list.
// Returns how many were copied.
JSBLE_API int32_t JsBle_DrainDiscovered(JsBleAdvertisement* outAdvertisements, int32_t maxCount)
{
    if (outAdvertisements == nullptr || maxCount <= 0)
    {
        return 0;
    }

    std::lock_guard<std::mutex> lock(g_ScanLock);
    const int32_t count = static_cast<int32_t>(std::min<size_t>(g_Discovered.size(), static_cast<size_t>(maxCount)));
    std::copy(g_Discovered.begin(), g_Discovered.begin() + count, outAdvertisements);
    g_Discovered.erase(g_Discovered.begin(), g_Discovered.begin() + count);
    return count;
}

// Connects, discovers the service's characteristics and subscribes to input. Blocking, and slow enough (a
// second or more) that it must never be called from the main thread. Returns a handle, or 0 on failure.
static int32_t ConnectImpl(uint64_t address, uint16_t productId)
{

    // A controller that fails to connect must be offered again, or one bad attempt would take it out of this
    // session entirely -- but not immediately: it advertises many times a second, and retrying on every one of
    // those would be a tight loop of failing connections.
    struct SuppressOnFailure
    {
        uint64_t address;
        bool succeeded = false;
        ~SuppressOnFailure()
        {
            if (!succeeded)
            {
                std::lock_guard<std::mutex> lock(g_ScanLock);
                g_SuppressedUntil[address] = Clock::now() + SuppressAfterFailure;
            }
        }
    } suppress{ address };

    auto connection = std::make_shared<Connection>();
    connection->address = address;
    connection->productId = productId;

    // Every way out short of success closes what was opened. Declared after the suppression, so it runs first:
    // Teardown clears the suppression, which the failure then sets again.
    struct TeardownOnFailure
    {
        const std::shared_ptr<Connection>& connection;
        const bool& succeeded;
        ~TeardownOnFailure()
        {
            if (!succeeded)
            {
                Teardown(connection);
            }
        }
    } teardownOnFailure{ connection, suppress.succeeded };

    try
    {
        connection->device = Await(BluetoothLEDevice::FromBluetoothAddressAsync(address));
        if (connection->device == nullptr)
        {
            Log(LogWarning, L"No Bluetooth device at address %012llX.", address);
            return 0;
        }

        // The session first: it is what asks Windows to bring the link up and keep it there. An uncached query
        // on a link that is not up yet comes back "unreachable" -- which is how a controller woken by a button
        // right after leaving its cable failed every attempt.
        connection->session = Await(GattSession::FromDeviceIdAsync(connection->device.BluetoothDeviceId()));
        if (connection->session != nullptr)
        {
            connection->session.MaintainConnection(true);
        }

        // Uncached: the controller was very likely connected to something else since Windows last looked, and a
        // stale service list produces characteristics whose handles no longer resolve. Retried while the link
        // comes up; the last try takes what Windows has cached rather than giving up.
        GattDeviceServicesResult servicesResult{ nullptr };
        for (int attempt = 0; attempt < 3; attempt++)
        {
            const BluetoothCacheMode mode = attempt < 2 ? BluetoothCacheMode::Uncached : BluetoothCacheMode::Cached;
            servicesResult = Await(connection->device.GetGattServicesForUuidAsync(ServiceUuid, mode));
            if (servicesResult.Status() == GattCommunicationStatus::Success && servicesResult.Services().Size() > 0)
            {
                break;
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(500));
        }
        if (servicesResult.Status() != GattCommunicationStatus::Success || servicesResult.Services().Size() == 0)
        {
            // Status: 0 success, 1 unreachable, 2 protocol error, 3 access denied.
            Log(LogWarning, L"Device %012llX: could not find the Switch 2 service (status %d, %u found).", address,
                static_cast<int>(servicesResult.Status()),
                servicesResult.Status() == GattCommunicationStatus::Success ? servicesResult.Services().Size() : 0u);
            return 0;
        }

        GattDeviceService service = servicesResult.Services().GetAt(0);
        connection->service = service;

        // Windows can refuse a process the service's contents while still listing the service itself: every
        // characteristic lookup then answers "access denied". Asking for access and opening the service shared
        // makes that refusal explicit, and the statuses say which of the two it was.
        try
        {
            const bool paired = connection->device.DeviceInformation().Pairing().IsPaired();
            const auto deviceAccess = Await(connection->device.RequestAccessAsync());
            const auto serviceAccess = Await(service.RequestAccessAsync());
            const auto openStatus = Await(service.OpenAsync(GattSharingMode::SharedReadAndWrite));
            // A sharing violation here means another process holds the service -- typically one that connected
            // and never closed it -- and every characteristic lookup below will then be denied.
            if (openStatus != GattOpenStatus::Success)
            {
                // DeviceAccessStatus: 0 unspecified, 1 allowed, 2 denied by user, 3 denied by system.
                // GattOpenStatus: 0 unspecified, 1 success, 2 already opened, 3 not found, 4 sharing violation,
                // 5 access denied.
                Log(LogWarning, L"Device %012llX: could not open the Switch 2 service (status %d; paired with Windows %d, "
                    L"device access %d, service access %d). Another application may be holding the controller.",
                    address, static_cast<int>(openStatus), paired ? 1 : 0, static_cast<int>(deviceAccess),
                    static_cast<int>(serviceAccess));
            }
        }
        catch (const winrt::hresult_error& error)
        {
            Log(LogInfo, L"Device %012llX: asking for access failed: %ls (0x%08X).", address, error.message().c_str(),
                static_cast<unsigned>(error.code()));
        }

        // Ask for the shortest connection interval the radio will give. This decides the polling rate: a BLE
        // peripheral can only speak once per connection interval, and Windows' default for a link it was told
        // nothing about is tens of milliseconds -- a controller with input ready every frame heard from a
        // handful of times a second. Throughput-optimised asks for 7.5ms. Not available on every Windows
        // version, and the radio is free to refuse; both cost latency and nothing else.
        try
        {
            connection->connectionParametersRequest = connection->device.RequestPreferredConnectionParameters(
                BluetoothLEPreferredConnectionParameters::ThroughputOptimized());
        }
        catch (...)
        {
            Log(LogInfo, L"Device %012llX kept the default connection interval; input may arrive more slowly.", address);
        }

        // Every characteristic the service reports, read once and only if a lookup by UUID comes back empty. A
        // lookup that fails says nothing about why; the full list, and the status of reading it, does.
        std::vector<GattCharacteristic> allCharacteristics;
        bool listedAll = false;
        auto listAll = [&]()
        {
            if (listedAll)
            {
                return;
            }
            listedAll = true;
            for (BluetoothCacheMode mode : { BluetoothCacheMode::Uncached, BluetoothCacheMode::Cached })
            {
                GattCharacteristicsResult result = Await(service.GetCharacteristicsAsync(mode));
                Log(LogInfo, L"Device %012llX: listing the service's characteristics (%ls) returned status %d with %u of them.",
                    address, mode == BluetoothCacheMode::Uncached ? L"uncached" : L"cached",
                    static_cast<int>(result.Status()), result.Status() == GattCommunicationStatus::Success ? result.Characteristics().Size() : 0u);
                if (result.Status() != GattCommunicationStatus::Success)
                {
                    continue;
                }
                for (const GattCharacteristic& characteristic : result.Characteristics())
                {
                    allCharacteristics.push_back(characteristic);
                    Log(LogInfo, L"  characteristic %ls, properties 0x%X",
                        winrt::to_hstring(characteristic.Uuid()).c_str(), static_cast<uint32_t>(characteristic.CharacteristicProperties()));
                }
                return;
            }
        };

        auto findCharacteristic = [&](const winrt::guid& uuid, const wchar_t* name, bool required) -> GattCharacteristic
        {
            GattCharacteristicsResult result = Await(service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode::Uncached));
            if (result.Status() == GattCommunicationStatus::Success && result.Characteristics().Size() > 0)
            {
                return result.Characteristics().GetAt(0);
            }

            if (required)
            {
                // Status: 0 success, 1 unreachable, 2 protocol error, 3 access denied.
                Log(LogInfo, L"Device %012llX: looking up the %ls characteristic returned status %d with %u matches.",
                    address, name, static_cast<int>(result.Status()),
                    result.Status() == GattCommunicationStatus::Success ? result.Characteristics().Size() : 0u);
            }

            listAll();
            for (const GattCharacteristic& characteristic : allCharacteristics)
            {
                if (characteristic.Uuid() == uuid)
                {
                    return characteristic;
                }
            }
            return GattCharacteristic{ nullptr };
        };

        connection->inputCharacteristic = findCharacteristic(InputReportUuid, L"input", true);
        connection->commandCharacteristic = findCharacteristic(CommandWriteUuid, L"command", true);
        connection->commandResponseCharacteristic = findCharacteristic(CommandResponseUuid, L"command response", true);
        if (connection->inputCharacteristic == nullptr || connection->commandCharacteristic == nullptr
            || connection->commandResponseCharacteristic == nullptr)
        {
            Log(LogWarning, L"Device %012llX is missing one of the Switch 2 characteristics.", address);
            return 0;
        }

        // The vibration characteristic is per controller shape. All three are tried rather than deciding from
        // the product id: a Joy-Con exposes only its own side, so whichever one is present is the right one.
        connection->vibrationCharacteristic = findCharacteristic(VibrationProUuid, L"vibration", false);
        if (connection->vibrationCharacteristic == nullptr)
        {
            connection->vibrationCharacteristic = findCharacteristic(VibrationJoyConLUuid, L"vibration", false);
        }
        if (connection->vibrationCharacteristic == nullptr)
        {
            connection->vibrationCharacteristic = findCharacteristic(VibrationJoyConRUuid, L"vibration", false);
        }

        // The handlers hold a weak reference and do nothing if they cannot take a strong one. Removing a
        // handler does not wait for one already running on a radio thread, so teardown and a notification do
        // overlap; pinning decides the race. Weak, not strong: a handler that owned the connection would keep
        // it alive for as long as WinRT held the delegate, which outlasts the disconnect.
        std::weak_ptr<Connection> weak = connection;

        connection->connectionStatusToken = connection->device.ConnectionStatusChanged(
            [weak](const BluetoothLEDevice& sender, const winrt::Windows::Foundation::IInspectable&)
            {
                if (auto pinned = weak.lock())
                {
                    if (sender.ConnectionStatus() == BluetoothConnectionStatus::Disconnected)
                    {
                        pinned->MarkDisconnected();
                    }
                }
            });

        connection->responseToken = connection->commandResponseCharacteristic.ValueChanged(
            [weak](const GattCharacteristic&, const GattValueChangedEventArgs& args)
            {
                auto pinned = weak.lock();
                if (!pinned)
                {
                    return;
                }
                std::vector<uint8_t> response = BufferToVector(args.CharacteristicValue());
                {
                    std::lock_guard<std::mutex> lock(pinned->responseLock);
                    if (pinned->responseQueue.size() >= Connection::MaxQueuedResponses)
                    {
                        pinned->responseQueue.pop_front();
                    }
                    pinned->responseQueue.push_back(std::move(response));
                }
                pinned->responseSignal.notify_all();
            });

        connection->inputToken = connection->inputCharacteristic.ValueChanged(
            [weak](const GattCharacteristic&, const GattValueChangedEventArgs& args)
            {
                auto pinned = weak.lock();
                if (!pinned)
                {
                    return;
                }
                std::vector<uint8_t> report = BufferToVector(args.CharacteristicValue());
                if (report.empty())
                {
                    return;
                }
                {
                    std::lock_guard<std::mutex> lock(pinned->inputLock);
                    if (pinned->inputQueue.size() >= Connection::MaxQueuedInputReports)
                    {
                        pinned->inputQueue.pop_front();
                    }
                    pinned->inputQueue.push_back(std::move(report));
                }
                pinned->inputSignal.notify_all();
            });

        // Subscribe to the response channel before input: the init commands that follow a connection are
        // answered on it, and a subscription that lands late loses the first acknowledgement.
        auto subscribe = [](const GattCharacteristic& characteristic) -> bool
        {
            return Await(characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue::Notify)) == GattCommunicationStatus::Success;
        };

        if (!subscribe(connection->commandResponseCharacteristic))
        {
            Log(LogWarning, L"Device %012llX refused the command-response subscription.", address);
            return 0;
        }
        if (!subscribe(connection->inputCharacteristic))
        {
            Log(LogWarning, L"Device %012llX refused the input subscription.", address);
            return 0;
        }

        {
            std::lock_guard<std::mutex> lock(g_ConnectionLock);
            connection->handle = g_NextHandle++;
            g_Connections[connection->handle] = connection;
        }

        Log(LogInfo, L"Connected to Switch 2 controller %012llX over Bluetooth.", address);
        suppress.succeeded = true;
        return connection->handle;
    }
    catch (const winrt::hresult_error& error)
    {
        Log(LogWarning, L"Bluetooth connection to %012llX failed: %ls", address, error.message().c_str());
        return 0;
    }
    catch (...)
    {
        Log(LogWarning, L"Bluetooth connection to %012llX failed.", address);
        return 0;
    }
}

// Ends the connection. The handle is invalid afterwards; a second call on it does nothing.
JSBLE_API void JsBle_Disconnect(int32_t handle)
{
    // Taking the connection out of the table is what claims it: whichever caller does that owns the teardown.
    ConnectionPtr owned;
    {
        std::lock_guard<std::mutex> lock(g_ConnectionLock);
        auto found = g_Connections.find(handle);
        if (found == g_Connections.end())
        {
            return;
        }
        owned = std::move(found->second);
        g_Connections.erase(found);
    }

    Teardown(owned);
    Log(LogInfo, L"Disconnected Switch 2 controller %012llX.", owned->address);
    // The memory is freed here only if nothing else is still inside this connection; a reading thread
    // mid-read holds the last reference and frees it when it returns.
}

// False once the link has dropped, which is how a reader learns the controller is gone.
JSBLE_API int32_t JsBle_IsConnected(int32_t handle)
{
    ConnectionPtr pinned = Pin(handle);
    return pinned && pinned->connected.load() ? 1 : 0;
}

// Fills up to maxCount live connection handles and returns how many there are. This is how a reloaded scripting
// domain finds the controllers the previous one connected.
JSBLE_API int32_t JsBle_GetConnections(int32_t* outHandles, int32_t maxCount)
{
    std::lock_guard<std::mutex> lock(g_ConnectionLock);
    int32_t count = 0;
    for (const auto& entry : g_Connections)
    {
        if (outHandles != nullptr && count < maxCount)
        {
            outHandles[count] = entry.first;
        }
        count++;
    }
    return count;
}

JSBLE_API int32_t JsBle_GetConnectionInfo(int32_t handle, uint64_t* outAddress, uint16_t* outProductId)
{
    ConnectionPtr pinned = Pin(handle);
    if (!pinned)
    {
        return 0;
    }
    if (outAddress != nullptr)
    {
        *outAddress = pinned->address;
    }
    if (outProductId != nullptr)
    {
        *outProductId = pinned->productId;
    }
    return 1;
}

static int32_t PhyCode(const BluetoothLEConnectionPhyInfo& info)
{
    return info.IsUncoded1MPhy() ? 1 : info.IsUncoded2MPhy() ? 2 : info.IsCodedPhy() ? 3 : 0;
}

// False on a Windows version without these queries, or once the connection has gone.
static int32_t GetLinkInfoImpl(int32_t handle, JsBleLinkInfo* outInfo)
{
    ConnectionPtr pinned = Pin(handle);
    if (!pinned || outInfo == nullptr || pinned->device == nullptr)
    {
        return 0;
    }

    try
    {
        const BluetoothLEConnectionParameters parameters = pinned->device.GetConnectionParameters();
        const BluetoothLEConnectionPhy phy = pinned->device.GetConnectionPhy();
        outInfo->connectionInterval = parameters.ConnectionInterval();
        outInfo->connectionLatency = parameters.ConnectionLatency();
        outInfo->linkTimeout = parameters.LinkTimeout();
        outInfo->transmitPhy = PhyCode(phy.TransmitInfo());
        outInfo->receivePhy = PhyCode(phy.ReceiveInfo());
        return 1;
    }
    catch (...)
    {
        return 0;
    }
}

// Whether Windows holds a link to this address, whoever asked for it: 1 yes, 0 no, -1 could not tell. Only looks:
// a device object without a session does not bring a link up. Blocks; call it from a worker thread.
static int32_t GetWindowsConnectionStatusImpl(uint64_t address)
{
    try
    {
        BluetoothLEDevice device = Await(BluetoothLEDevice::FromBluetoothAddressAsync(address));
        if (device == nullptr)
        {
            return -1;
        }
        const int32_t status = device.ConnectionStatus() == BluetoothConnectionStatus::Connected ? 1 : 0;
        device.Close();
        return status;
    }
    catch (...)
    {
        return -1;
    }
}

// Sends one command and waits for the controller's acknowledgement, which is how a command reports whether it
// was accepted. Commands are serialised: the response characteristic carries no request id, so two commands in
// flight could not be told apart.
//
// The response payload (what follows the 8-byte header) is copied to outResponse, and its full length written
// to outResponseLength. minResponseBytes is for a caller that wants a payload rather than just a yes: a memory
// read is answered with more than one notification, and the acknowledgement -- eight bytes, correct command
// id, status accepted -- passes every test a payload does except being the payload. Saying how much is
// expected is what lets the ack be recognised as not the answer and the wait continue.
static int32_t SendCommandImpl(int32_t handle, uint8_t commandId, uint8_t subcommandId,
    const uint8_t* data, int32_t dataLength, uint8_t* outResponse, int32_t responseCapacity,
    int32_t* outResponseLength, int32_t minResponseBytes, int32_t timeoutMs)
{
    if (outResponseLength != nullptr)
    {
        *outResponseLength = 0;
    }

    // Pinned for the whole exchange, which includes a wait of up to timeoutMs. Without it a controller that
    // disconnected mid-command would be freed while this thread is still waiting on it.
    ConnectionPtr pinned = Pin(handle);
    if (!pinned || !pinned->connected.load() || pinned->commandCharacteristic == nullptr || dataLength < 0 || dataLength > 255)
    {
        return 0;
    }


    // Eight-byte header, then the payload: [command][0x91][transport][subcommand][0][length][0][0]. Transport
    // 0x01 is Bluetooth -- the same command with USB's 0x00 makes the controller go looking for a Bluetooth
    // host instead of answering.
    std::vector<uint8_t> packet(8 + static_cast<size_t>(dataLength), 0);
    packet[0] = commandId;
    packet[1] = 0x91;
    packet[2] = 0x01;
    packet[3] = subcommandId;
    packet[5] = static_cast<uint8_t>(dataLength);
    if (data != nullptr && dataLength > 0)
    {
        std::memcpy(packet.data() + 8, data, static_cast<size_t>(dataLength));
    }

    std::lock_guard<std::mutex> commandLock(pinned->commandLock);

    {
        // Anything still queued belongs to an earlier exchange -- an answer that arrived after its sender gave
        // up. Dropped rather than examined, because the only thing it can do now is be mistaken for this one.
        std::lock_guard<std::mutex> lock(pinned->responseLock);
        pinned->responseQueue.clear();
    }

    try
    {
        const GattCommunicationStatus status = Await(pinned->commandCharacteristic.WriteValueAsync(
            ToBuffer(packet.data(), static_cast<int32_t>(packet.size()))), 5);
        if (status != GattCommunicationStatus::Success)
        {
            return 0;
        }
    }
    catch (...)
    {
        return 0;
    }

    // Wait for an answer that fits what was asked, not merely for the next notification. Status byte 0x01 is
    // the controller's "accepted". Anything else on the channel is discarded and the wait resumes on what is
    // left of the deadline.
    const auto deadline = Clock::now() + std::chrono::milliseconds(std::max(timeoutMs, 0));
    const size_t minimumLength = static_cast<size_t>(std::max(8, minResponseBytes));
    std::unique_lock<std::mutex> lock(pinned->responseLock);
    for (;;)
    {
        while (!pinned->responseQueue.empty())
        {
            std::vector<uint8_t> candidate = std::move(pinned->responseQueue.front());
            pinned->responseQueue.pop_front();
            if (candidate.size() < minimumLength || candidate[0] != commandId || candidate[1] != 0x01)
            {
                continue;
            }

            const int32_t payloadLength = static_cast<int32_t>(candidate.size() - 8);
            if (outResponse != nullptr && responseCapacity > 0)
            {
                std::memcpy(outResponse, candidate.data() + 8, static_cast<size_t>(std::min(payloadLength, responseCapacity)));
            }
            if (outResponseLength != nullptr)
            {
                *outResponseLength = payloadLength;
            }
            return 1;
        }

        // Checked after the drain, so an answer that arrived just before the link dropped is still taken.
        if (!pinned->connected.load() || Clock::now() >= deadline)
        {
            return 0;
        }

        pinned->responseSignal.wait_until(lock, deadline);
    }
}

// Writes bytes to the command characteristic exactly as given, with no header and no acknowledgement. This
// exists for the one packet that does not fit the command shape -- the Joy-Con 2's input-mode selection, whose
// second byte is not the 0x91 every other command carries.
static int32_t SendRawCommandImpl(int32_t handle, const uint8_t* data, int32_t length)
{
    ConnectionPtr pinned = Pin(handle);
    if (!pinned || !pinned->connected.load() || pinned->commandCharacteristic == nullptr || data == nullptr || length <= 0)
    {
        return 0;
    }


    // Serialised against SendCommand even though nothing is awaited: the controller answers on one response
    // channel, and an unheadered write between a command and its acknowledgement would leave that
    // acknowledgement looking like the answer to the wrong thing.
    std::lock_guard<std::mutex> commandLock(pinned->commandLock);
    try
    {
        return Await(pinned->commandCharacteristic.WriteValueAsync(ToBuffer(data, length)), 5)
            == GattCommunicationStatus::Success ? 1 : 0;
    }
    catch (...)
    {
        return 0;
    }
}

// Writes HD rumble frames to the vibration characteristic. Unacknowledged and not awaited: an acknowledged
// write at rumble rate would queue behind itself and starve the input notifications sharing the link, and the
// next packet is due in 15ms anyway.
JSBLE_API int32_t JsBle_SendVibration(int32_t handle, const uint8_t* payload, int32_t length)
{
    ConnectionPtr pinned = Pin(handle);
    if (!pinned || !pinned->connected.load() || pinned->vibrationCharacteristic == nullptr || payload == nullptr || length <= 0)
    {
        return 0;
    }

    try
    {
        auto operation = pinned->vibrationCharacteristic.WriteValueAsync(
            ToBuffer(payload, length), GattWriteOption::WriteWithoutResponse);
        // The completion handler is not optional: an async operation whose failure nobody observes raises its
        // exception on a WinRT thread, where there is no catch to meet it.
        operation.Completed([](const winrt::Windows::Foundation::IAsyncOperation<GattCommunicationStatus>&,
            winrt::Windows::Foundation::AsyncStatus) {});
        return 1;
    }
    catch (...)
    {
        return 0;
    }
}

// Pops one input report, waiting up to timeoutMs for one to arrive. Returns its length, 0 on timeout, and -1
// once the connection is gone. A GATT notification carries the report body alone: the same report over USB
// arrives behind a HID report-id byte, so every field here sits one byte earlier.
JSBLE_API int32_t JsBle_ReadInputReport(int32_t handle, uint8_t* outBuffer, int32_t maxLength, int32_t timeoutMs)
{
    // The reference keeps the connection alive until the read returns, so the worst a concurrent disconnect
    // can do is end the wait early with -1.
    ConnectionPtr pinned = Pin(handle);
    if (!pinned || outBuffer == nullptr || maxLength <= 0)
    {
        return -1;
    }

    const auto deadline = Clock::now() + std::chrono::milliseconds(std::max(timeoutMs, 0));
    std::unique_lock<std::mutex> lock(pinned->inputLock);
    for (;;)
    {
        if (!pinned->inputQueue.empty())
        {
            std::vector<uint8_t> report = std::move(pinned->inputQueue.front());
            pinned->inputQueue.pop_front();
            const int32_t length = std::min(static_cast<int32_t>(report.size()), maxLength);
            std::memcpy(outBuffer, report.data(), static_cast<size_t>(length));
            return length;
        }

        // Checked after the queue, not before: reports already delivered are worth reading out even once the
        // link has dropped.
        if (!pinned->connected.load())
        {
            return -1;
        }
        if (Clock::now() >= deadline)
        {
            return 0;
        }

        pinned->inputSignal.wait_until(lock, deadline);
    }
}

// Pops one queued log line into buffer (always terminated) and returns its level, or -1 when none is queued.
JSBLE_API int32_t JsBle_PopLog(wchar_t* buffer, int32_t capacity)
{
    std::lock_guard<std::mutex> lock(g_LogLock);
    if (g_Log.empty() || buffer == nullptr || capacity <= 0)
    {
        return -1;
    }
    const auto entry = std::move(g_Log.front());
    g_Log.pop_front();
    wcsncpy_s(buffer, static_cast<size_t>(capacity), entry.second.c_str(), _TRUNCATE);
    return entry.first;
}

// Stops scanning and releases every connection. Called when the application quits.
JSBLE_API void JsBle_Shutdown()
{
    JsBle_StopScan();

    std::vector<int32_t> handles;
    {
        std::lock_guard<std::mutex> lock(g_ConnectionLock);
        for (const auto& entry : g_Connections)
        {
            handles.push_back(entry.first);
        }
    }
    for (int32_t handle : handles)
    {
        JsBle_Disconnect(handle);
    }
}

JSBLE_API int32_t JsBle_IsSupported()
{
    return RunOnMta([&] { return IsSupportedImpl(); });
}

JSBLE_API uint64_t JsBle_GetHostAddress()
{
    return RunOnMta([&] { return GetHostAddressImpl(); });
}

JSBLE_API int32_t JsBle_StartScan()
{
    return RunOnMta([&] { return StartScanImpl(); });
}

JSBLE_API int32_t JsBle_Connect(uint64_t address, uint16_t productId)
{
    return RunOnMta([&] { return ConnectImpl(address, productId); });
}

JSBLE_API int32_t JsBle_GetLinkInfo(int32_t handle, JsBleLinkInfo* outInfo)
{
    return RunOnMta([&] { return GetLinkInfoImpl(handle, outInfo); });
}

JSBLE_API int32_t JsBle_GetWindowsConnectionStatus(uint64_t address)
{
    return RunOnMta([&] { return GetWindowsConnectionStatusImpl(address); });
}

JSBLE_API int32_t JsBle_SendRawCommand(int32_t handle, const uint8_t* data, int32_t length)
{
    return RunOnMta([&] { return SendRawCommandImpl(handle, data, length); });
}

JSBLE_API int32_t JsBle_SendCommand(int32_t handle, uint8_t commandId, uint8_t subcommandId,
    const uint8_t* data, int32_t dataLength, uint8_t* outResponse, int32_t responseCapacity,
    int32_t* outResponseLength, int32_t minResponseBytes, int32_t timeoutMs)
{
    return RunOnMta([&] { return SendCommandImpl(handle, commandId, subcommandId, data, dataLength, outResponse,
        responseCapacity, outResponseLength, minResponseBytes, timeoutMs); });
}
