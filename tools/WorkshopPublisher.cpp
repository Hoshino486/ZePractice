// Small official Steamworks client helper. Uses the signed-in Steam client,
// never reads passwords/cookies and never creates a new Workshop item.
#include <steam/steam_api.h>
#include <chrono>
#include <thread>
#include <iostream>
#include <string>
#include <stdexcept>
#include <filesystem>
#define NOMINMAX
#include <windows.h>

constexpr PublishedFileId_t Item = 3818340815ULL;
template<class T> T Wait(SteamAPICall_t call, int timeout = 180, UGCUpdateHandle_t update = k_UGCUpdateHandleInvalid) {
    if (!call) throw std::runtime_error("Steam returned an invalid API call");
    auto until = std::chrono::steady_clock::now() + std::chrono::seconds(timeout);
    bool failed = false;
    auto progressAt = std::chrono::steady_clock::now();
    while (!SteamUtils()->IsAPICallCompleted(call, &failed)) {
        SteamAPI_RunCallbacks();
        if (std::chrono::steady_clock::now() >= until) throw std::runtime_error("Steam API request timed out");
        if (update != k_UGCUpdateHandleInvalid && std::chrono::steady_clock::now() >= progressAt) {
            uint64 processed = 0, total = 0;
            auto status = SteamUGC()->GetItemUpdateProgress(update, &processed, &total);
            std::cout << "UPLOAD_PROGRESS status=" << status << " processed=" << processed << " total=" << total << std::endl;
            progressAt = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
    T value{};
    if (failed || !SteamUtils()->GetAPICallResult(call, &value, sizeof(value), T::k_iCallback, &failed) || failed)
        throw std::runtime_error("Steam API callback failed");
    return value;
}
SteamUGCDetails_t Details() {
    PublishedFileId_t item = Item;
    auto handle = SteamUGC()->CreateQueryUGCDetailsRequest(&item, 1);
    if (handle == k_UGCQueryHandleInvalid) throw std::runtime_error("Invalid details query");
    SteamUGC()->SetAllowCachedResponse(handle, 0);
    auto result = Wait<SteamUGCQueryCompleted_t>(SteamUGC()->SendQueryUGCRequest(handle));
    SteamUGCDetails_t details{};
    bool ok = result.m_eResult == k_EResultOK && result.m_unNumResultsReturned == 1 && SteamUGC()->GetQueryUGCResult(handle, 0, &details);
    // New clients can select Workshop content by game branch. Inspect the current
    // public flat API without changing the legacy SDK interface's vtable layout.
    HMODULE api = GetModuleHandleW(L"steam_api64.dll");
    auto latest = reinterpret_cast<void*(*)()>(GetProcAddress(api, "SteamAPI_SteamUGC_v021"));
    auto countVersions = reinterpret_cast<uint32(*)(void*, UGCQueryHandle_t, uint32)>(GetProcAddress(api, "SteamAPI_ISteamUGC_GetNumSupportedGameVersions"));
    auto versionData = reinterpret_cast<bool(*)(void*, UGCQueryHandle_t, uint32, uint32, char*, char*, uint32)>(GetProcAddress(api, "SteamAPI_ISteamUGC_GetSupportedGameVersionData"));
    if (ok && latest && countVersions && versionData) {
        void* ugc = latest(); auto count = countVersions(ugc, handle, 0);
        std::cout << "VERSIONS count=" << count << std::endl;
        for (uint32 i = 0; i < count && i < 64; ++i) {
            char min[256]{}, max[256]{};
            if (versionData(ugc, handle, 0, i, min, max, sizeof(min)))
                std::cout << "VERSION index=" << i << " min=" << min << " max=" << max << std::endl;
        }
    }
    SteamUGC()->ReleaseQueryUGCRequest(handle);
    if (!ok || details.m_eResult != k_EResultOK || details.m_nPublishedFileId != Item || details.m_nConsumerAppID != 730)
        throw std::runtime_error("Existing CS2 resource item lookup failed");
    return details;
}
void PrintDetails(const SteamUGCDetails_t& d) {
    std::cout << "DETAILS item=" << d.m_nPublishedFileId << " app=" << d.m_nConsumerAppID
      << " updated=" << d.m_rtimeUpdated << " bytes=" << d.m_nFileSize
      << " ownerMatches=" << (d.m_ulSteamIDOwner == SteamUser()->GetSteamID().ConvertToUint64())
      << " banned=" << d.m_bBanned << " content=" << d.m_hFile << std::endl;
}
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc < 2) throw std::runtime_error("Usage: WorkshopPublisher inspect|upload CONTENT_FOLDER|download");
        if (!SteamAPI_Init()) throw std::runtime_error("Steam client initialization failed (appid 730)");
        if (!SteamUser()->BLoggedOn() || !SteamUGC()) throw std::runtime_error("A logged-in Steam client is required");
        auto before = Details(); PrintDetails(before);
        std::wstring mode = argv[1];
        if (mode == L"upload") {
            if (argc != 3 || !std::filesystem::is_directory(argv[2])) throw std::runtime_error("Content directory missing");
            if (before.m_ulSteamIDOwner != SteamUser()->GetSteamID().ConvertToUint64()) throw std::runtime_error("Current Steam user is not the existing item owner");
            if (before.m_bBanned) throw std::runtime_error("Workshop item is banned");
            // Windows argv is UTF-16; Steamworks paths are UTF-8, not the system ANSI code page.
            // Passing a Chinese workspace path through char** makes Steam report "no content".
            const auto contentPath = std::filesystem::absolute(std::filesystem::path(argv[2])).u8string();
            auto update = SteamUGC()->StartItemUpdate(730, Item);
            if (update == k_UGCUpdateHandleInvalid || !SteamUGC()->SetItemContent(update, contentPath.c_str())) throw std::runtime_error("Start/set content update failed");
            // Make the resource bundle available to the public game/server branch,
            // rather than inheriting an implicit version selection from an old SDK.
            auto api = GetModuleHandleW(L"steam_api64.dll");
            auto latest = reinterpret_cast<void*(*)()>(GetProcAddress(api, "SteamAPI_SteamUGC_v021"));
            auto setVersions = reinterpret_cast<bool(*)(void*, UGCUpdateHandle_t, const char*, const char*)>(GetProcAddress(api, "SteamAPI_ISteamUGC_SetRequiredGameVersions"));
            if (latest && setVersions && !setVersions(latest(), update, "", "")) throw std::runtime_error("Workshop all-branch compatibility update rejected");
            // Only content + changelog. Title, visibility, tags, preview and description are preserved.
            auto result = Wait<SubmitItemUpdateResult_t>(SteamUGC()->SubmitItemUpdate(update,
              "saibochana_Server 1.7.8: use client-known chat commands for Workshop radio input, no command-filter disabling launch option required. Keep menu actions and paging open; key 9 closes without reopening the input menu. Preserves Luna character/arms, markers, glowing countdown digits and HUDs. No map scripts changed."), 1200, update);
            std::cout << "SUBMIT item=" << result.m_nPublishedFileId << " result=" << result.m_eResult
              << " needsAgreement=" << result.m_bUserNeedsToAcceptWorkshopLegalAgreement << std::endl;
            if (result.m_eResult != k_EResultOK || result.m_nPublishedFileId != Item || result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
                throw std::runtime_error("Workshop update not confirmed ready");
            PrintDetails(Details());
        } else if (mode == L"download") {
            if (!SteamUGC()->DownloadItem(Item, true)) throw std::runtime_error("Steam rejected download request");
            auto until = std::chrono::steady_clock::now() + std::chrono::minutes(10);
            auto refresh = std::chrono::steady_clock::now() + std::chrono::seconds(30);
            bool ready = false;
            while (std::chrono::steady_clock::now() < until) {
                SteamAPI_RunCallbacks();
                if (std::chrono::steady_clock::now() >= refresh) {
                    SteamUGC()->DownloadItem(Item, true);
                    refresh = std::chrono::steady_clock::now() + std::chrono::seconds(30);
                }
                auto state = SteamUGC()->GetItemState(Item);
                uint64 installedBytes = 0; uint32 installedTime = 0; char installedPath[4096]{};
                if ((state & k_EItemStateInstalled) && !(state & (k_EItemStateNeedsUpdate | k_EItemStateDownloading | k_EItemStateDownloadPending)) &&
                    SteamUGC()->GetItemInstallInfo(Item, &installedBytes, installedPath, sizeof(installedPath), &installedTime) && installedTime >= before.m_rtimeUpdated)
                    { ready = true; break; }
                std::this_thread::sleep_for(std::chrono::milliseconds(500));
            }
            uint64 bytes = 0; uint32 time = 0; char path[4096]{};
            const bool installed = SteamUGC()->GetItemInstallInfo(Item, &bytes, path, sizeof(path), &time);
            if (!ready || !installed) {
                // A successful DownloadItem callback can still resolve to an older
                // published revision. Preserve evidence rather than declaring success.
                std::cerr << "STALE_INSTALL state=" << SteamUGC()->GetItemState(Item)
                  << " info=" << installed << " bytes=" << bytes << " timestamp=" << time
                  << " expectedTimestamp=" << before.m_rtimeUpdated << " path=" << path << std::endl;
                throw std::runtime_error("Downloaded install has not reached the published revision; hash verification still required");
            }
            std::cout << "INSTALLED item=" << Item << " bytes=" << bytes << " timestamp=" << time << " path=" << path << std::endl;
        } else if (mode != L"inspect") throw std::runtime_error("Unknown mode");
        SteamAPI_Shutdown(); return 0;
    } catch (const std::exception& e) { std::cerr << "ERROR " << e.what() << std::endl; SteamAPI_Shutdown(); return 1; }
}
