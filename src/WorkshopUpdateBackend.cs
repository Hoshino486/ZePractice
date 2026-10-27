using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace ZePractice;

public sealed class WorkshopUpdateBackend(string gameDirectory) : IWorkshopUpdateBackend, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
    private IntPtr _library;
    private GetUgc? _getUgc;
    private GetState? _getState;
    private GetInstalled? _getInstalled;
    private DownloadItem? _download;
    private GetProgress? _progress;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetUgc();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetState(IntPtr api, ulong identifier);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetInstalled(IntPtr api, ulong identifier, out ulong size, [Out] byte[] folder, uint capacity, out uint updated);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool DownloadItem(IntPtr api, ulong identifier, [MarshalAs(UnmanagedType.I1)] bool priority);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool GetProgress(IntPtr api, ulong identifier, out ulong downloaded, out ulong total);

    public async Task<IReadOnlyList<WorkshopRevision>> QueryAsync(string[] identifiers, CancellationToken cancellation)
    {
        var revisions = new List<WorkshopRevision>();
        foreach (string[] batch in identifiers.Chunk(100))
        {
            var fields = new List<KeyValuePair<string, string>> { new("itemcount", batch.Length.ToString(CultureInfo.InvariantCulture)) };
            for (int index = 0; index < batch.Length; index++) fields.Add(new($"publishedfileids[{index}]", batch[index]));
            using var content = new FormUrlEncodedContent(fields);
            using var response = await _http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", content, cancellation).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
            revisions.AddRange(WorkshopUpdateCatalog.Parse(json, batch));
        }
        return revisions;
    }

    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    private IntPtr Interface()
    {
        if (_library == IntPtr.Zero)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Workshop auto-update is configured for this Windows dedicated server.");
            string root = gameDirectory;
            if (Path.GetFileName(root).Equals("csgo", StringComparison.OrdinalIgnoreCase)) root = Directory.GetParent(root)!.FullName;
            _library = NativeLibrary.Load(Path.Combine(root, "bin", "win64", "steam_api64.dll"));
            try
            {
                _getUgc = Export<GetUgc>("SteamAPI_SteamGameServerUGC_v021");
                _getState = Export<GetState>("SteamAPI_ISteamUGC_GetItemState");
                _getInstalled = Export<GetInstalled>("SteamAPI_ISteamUGC_GetItemInstallInfo");
                _download = Export<DownloadItem>("SteamAPI_ISteamUGC_DownloadItem");
                _progress = Export<GetProgress>("SteamAPI_ISteamUGC_GetItemDownloadInfo");
            }
            catch { NativeLibrary.Free(_library); _library = IntPtr.Zero; throw; }
        }
        IntPtr api = _getUgc!();
        return api != IntPtr.Zero ? api : throw new InvalidOperationException("Steam GameServer UGC is not ready; waiting for online initialization.");
    }

    public WorkshopInstallation Inspect(string identifier)
    {
        if (!WorkshopUpdateCatalog.ValidId(identifier)) throw new ArgumentException("Invalid Workshop ID.");
        IntPtr api = Interface(); ulong id = ulong.Parse(identifier, CultureInfo.InvariantCulture);
        uint state = _getState!(api, id);
        _progress!(api,id,out ulong downloaded,out ulong total);
        if (!_getInstalled!(api, id, out ulong size, new byte[4096], 4096, out uint updated)) return new(state & ~4u, 0, 0, downloaded,total);
        return new(state, updated, size, downloaded,total);
    }

    public bool Download(string identifier)
    {
        if (!WorkshopUpdateCatalog.ValidId(identifier)) throw new ArgumentException("Invalid Workshop ID.");
        IntPtr api = Interface();
        return _download!(api, ulong.Parse(identifier, CultureInfo.InvariantCulture), false);
    }

    public void Dispose()
    {
        _http.Dispose();
        if (_library != IntPtr.Zero) { NativeLibrary.Free(_library); _library = IntPtr.Zero; }
    }
}
