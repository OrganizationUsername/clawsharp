using System.Runtime.InteropServices;
using Clawsharp.Config;
using Microsoft.Extensions.Logging;

namespace Clawsharp.Security;

/// <summary>
/// Applies Linux Landlock LSM filesystem restrictions to the gateway process.
/// Must be called once at startup before the Generic Host is built.
/// Silently no-ops on non-Linux platforms or when the kernel does not support Landlock.
///
/// Landlock is a Linux 5.13+ unprivileged sandboxing mechanism that restricts
/// which filesystem paths the calling process (and all descendant threads/processes)
/// can access. Restrictions are permanent -- they cannot be removed once applied.
///
/// Design: static class (not a hosted service) because Landlock must be applied
/// before the DI container boots and any async work starts. This ensures the
/// main thread (and all subsequently created thread pool threads) inherit restrictions.
/// </summary>
public static partial class LandlockSandbox
{
    // ── Syscall numbers (same on x86-64 and aarch64) ──────────────────────
    private const long SysLandlockCreateRuleset = 444L;

    private const long SysLandlockAddRule = 445L;

    private const long SysLandlockRestrictSelf = 446L;

    // ── Landlock flags ────────────────────────────────────────────────────
    private const long LandlockCreateRulesetVersion = 1L;

    private const long LandlockRulePathBeneath = 1L;

    // ── Filesystem access bitmasks (from kernel UAPI headers) ─────────────
    private const ulong FsExecute = 1UL << 0;

    private const ulong FsWriteFile = 1UL << 1;

    private const ulong FsReadFile = 1UL << 2;

    private const ulong FsReadDir = 1UL << 3;

    private const ulong FsRemoveDir = 1UL << 4;

    private const ulong FsRemoveFile = 1UL << 5;

    private const ulong FsMakeChar = 1UL << 6;

    private const ulong FsMakeDir = 1UL << 7;

    private const ulong FsMakeReg = 1UL << 8;

    private const ulong FsMakeSock = 1UL << 9;

    private const ulong FsMakeFifo = 1UL << 10;

    private const ulong FsMakeBlock = 1UL << 11;

    private const ulong FsMakeSym = 1UL << 12;

    private const ulong FsRefer = 1UL << 13; // ABI v2+

    private const ulong FsTruncate = 1UL << 14; // ABI v3+

    private const ulong FsIoctlDev = 1UL << 15; // ABI v5+

    // ── Convenience composites ────────────────────────────────────────────
    private const ulong AccessRead = FsReadFile | FsReadDir;

    private const ulong AccessExecute = FsExecute | FsReadFile | FsReadDir;

    private const ulong AccessReadWrite = FsReadFile | FsWriteFile | FsReadDir
                                          | FsRemoveFile | FsRemoveDir
                                          | FsMakeReg | FsMakeDir | FsMakeSym
                                          | FsTruncate; // masked to ABI at runtime

    // ── prctl ─────────────────────────────────────────────────────────────
    private const int PrSetNoNewPrivs = 38;

    // ── open(2) flags ─────────────────────────────────────────────────────
    private const int OPath = 0x200000;

    private const int OCloexec = 0x080000;

    // ── P/Invoke declarations (LibraryImport = AOT-safe source gen) ───────

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long SyscallProbe(long nr, nint a1, nint a2, long a3);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long SyscallCreate(long nr, nint attr, nint attrSize, long flags);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long SyscallAddRule(long nr, long rulesetFd, long ruleType, nint ruleAttr, long flags);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long SyscallRestrictSelf(long nr, long rulesetFd, long flags);

    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int Prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenPath(string pathname, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseFd(int fd);

    // ── Kernel structs ────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct LandlockRulesetAttr
    {
        public ulong HandledAccessFs;

        public ulong HandledAccessNet; // zero for ABI < 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LandlockPathBeneathAttr
    {
        public ulong AllowedAccess;

        public int ParentFd;
#pragma warning disable CS0169 // Field is never used -- required 4-byte pad to 16-byte struct
        private int _pad;
#pragma warning restore CS0169
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Apply Landlock filesystem restrictions to the current process.
    /// Call once before the Generic Host is built. No-op on non-Linux
    /// or if <paramref name="config"/>.<see cref="LandlockConfig.Enabled"/> is false.
    /// Never throws -- all failures are logged as warnings and startup continues.
    /// </summary>
    /// <param name="config">Landlock configuration from <c>security.landlock</c>.</param>
    /// <param name="logger">Logger for diagnostics. May be null for silent operation.</param>
    /// <param name="shellEnabled">
    /// When true, grants execute access to <c>/bin</c>, <c>/sbin</c>, <c>/usr/bin</c>, and <c>/usr/sbin</c>
    /// so the shell tool can invoke system executables. When false, these paths are omitted from
    /// the Landlock ruleset, preventing shell execution even if the process has other access.
    /// Default: true (preserves existing behavior).
    /// </param>
    public static void Apply(LandlockConfig config, ILogger? logger, bool shellEnabled = true)
    {
        if (!config.Enabled)
        {
            if (logger is not null)
            {
                LogSandboxDisabled(logger);
            }

            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            if (logger is not null)
            {
                LogSandboxSkippedNotLinux(logger);
            }

            return;
        }

        try
        {
            ApplyCore(config, logger, shellEnabled);
        }
        catch (Exception ex)
        {
            // Never crash startup due to Landlock failure
            if (logger is not null)
            {
                LogSandboxUnexpectedError(logger, ex);
            }
        }
    }

    // ── Core implementation ───────────────────────────────────────────────

    private static void ApplyCore(LandlockConfig config, ILogger? logger, bool shellEnabled)
    {
        // Step 1: Probe ABI version
        long abiLong = SyscallProbe(SysLandlockCreateRuleset, nint.Zero, nint.Zero,
            LandlockCreateRulesetVersion);
        if (abiLong < 1)
        {
            int errno = Marshal.GetLastPInvokeError();
            string reason = errno switch
            {
                95 => "disabled at boot (security.landlock=off in kernel cmdline)",
                38 => "kernel too old (requires Linux 5.13+)",
                _ => $"errno {errno}",
            };
            if (logger is not null)
            {
                LogSandboxUnavailable(logger, reason);
            }

            return;
        }

        int abi = (int)abiLong;
        if (logger is not null)
        {
            LogSandboxAbiDetected(logger, abi);
        }

        // Step 2: Build handled_access_fs for this ABI
        ulong handledFs = BuildHandledFs(abi);

        // Step 3: Create ruleset
        int rulesetFd = CreateRuleset(handledFs);
        if (rulesetFd < 0)
        {
            if (logger is not null)
            {
                LogCreateRulesetFailed(logger, Marshal.GetLastPInvokeError());
            }

            return;
        }

        int rulesAdded = 0;
        try
        {
            // Step 4: Build and add path rules
            ulong rwAccess = MaskToAbi(AccessReadWrite, abi);
            ulong roAccess = MaskToAbi(AccessRead, abi);
            ulong execAccess = MaskToAbi(AccessExecute, abi);

            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string clawsharpDir = ConfigLoader.ExpandHome("~/.clawsharp");

            // ~/.clawsharp/ — sessions, memory, config, audit, costs
            rulesAdded += AddPathRule(rulesetFd, clawsharpDir, rwAccess, logger);

            // /tmp — temp files, IPC sockets
            rulesAdded += AddPathRule(rulesetFd, "/tmp", rwAccess, logger);

            // /usr — .NET runtime, system libraries, locale, timezone data
            rulesAdded += AddPathRule(rulesetFd, "/usr", execAccess, logger);

            // /lib, /lib64 — shared libraries (may be symlinks to /usr/lib)
            rulesAdded += AddPathRule(rulesetFd, "/lib", roAccess, logger);
            rulesAdded += AddPathRule(rulesetFd, "/lib64", roAccess, logger);

            // /bin, /sbin — shell executables (only when ShellTool is enabled)
            if (shellEnabled)
            {
                rulesAdded += AddPathRule(rulesetFd, "/bin", execAccess, logger);
                rulesAdded += AddPathRule(rulesetFd, "/sbin", execAccess, logger);
                rulesAdded += AddPathRule(rulesetFd, "/usr/bin", execAccess, logger);
                rulesAdded += AddPathRule(rulesetFd, "/usr/sbin", execAccess, logger);
            }

            // /etc — SSL certs, resolv.conf, hosts, localtime, passwd
            rulesAdded += AddPathRule(rulesetFd, "/etc", roAccess, logger);

            // /proc/self — .NET runtime self-inspection
            rulesAdded += AddPathRule(rulesetFd, "/proc/self", roAccess, logger);

            // /run/secrets — Docker secrets
            rulesAdded += AddPathRule(rulesetFd, "/run/secrets", roAccess, logger);

            // ~/.dotnet — per-user .NET installations
            string dotnetHome = Path.Combine(homeDir, ".dotnet");
            rulesAdded += AddPathRule(rulesetFd, dotnetHome, execAccess, logger);

            // Additional configured read-only paths
            if (config.AdditionalReadPaths is { Length: > 0 })
            {
                foreach (string p in config.AdditionalReadPaths)
                {
                    string expanded = ConfigLoader.ExpandHome(p);
                    rulesAdded += AddPathRule(rulesetFd, expanded, roAccess, logger);
                }
            }

            // Additional configured read+write paths
            if (config.AdditionalReadWritePaths is { Length: > 0 })
            {
                foreach (string p in config.AdditionalReadWritePaths)
                {
                    string expanded = ConfigLoader.ExpandHome(p);
                    rulesAdded += AddPathRule(rulesetFd, expanded, rwAccess, logger);
                }
            }

            // Step 5: PR_SET_NO_NEW_PRIVS (required before restrict_self)
            if (Prctl(PrSetNoNewPrivs, 1, 0, 0, 0) != 0)
            {
                if (logger is not null)
                {
                    LogPrctlFailed(logger, Marshal.GetLastPInvokeError());
                }

                return;
            }

            // Step 6: Apply restrictions to this thread (and all future children)
            long ret = SyscallRestrictSelf(SysLandlockRestrictSelf, rulesetFd, 0L);
            if (ret != 0)
            {
                if (logger is not null)
                {
                    LogRestrictSelfFailed(logger, Marshal.GetLastPInvokeError());
                }

                return;
            }

            if (logger is not null)
            {
                LogSandboxActive(logger, rulesAdded, abi);
            }
        }
        finally
        {
            CloseFd(rulesetFd);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the <c>handled_access_fs</c> bitmask appropriate for the detected ABI version.
    /// Only includes bits the kernel understands; unknown bits cause EINVAL.
    /// </summary>
    private static ulong BuildHandledFs(int abi)
    {
        // v1 baseline: bits 0-12
        ulong mask = 0x1FFFUL;

        if (abi >= 2)
        {
            mask |= FsRefer; // bit 13
        }

        if (abi >= 3)
        {
            mask |= FsTruncate; // bit 14
        }

        if (abi >= 5)
        {
            mask |= FsIoctlDev; // bit 15
        }

        return mask;
    }

    /// <summary>
    /// Mask a convenience access constant to only include bits supported by the detected ABI.
    /// </summary>
    private static ulong MaskToAbi(ulong access, int abi)
    {
        return access & BuildHandledFs(abi);
    }

    /// <summary>Create a Landlock ruleset file descriptor.</summary>
    private static unsafe int CreateRuleset(ulong handledAccessFs)
    {
        var attr = new LandlockRulesetAttr
        {
            HandledAccessFs = handledAccessFs,
            HandledAccessNet = 0,
        };
        nint attrPtr = (nint)(&attr);
        nint attrSize = (nint)sizeof(LandlockRulesetAttr);
        return (int)SyscallCreate(SysLandlockCreateRuleset, attrPtr, attrSize, 0L);
    }

    /// <summary>
    /// Add a path-beneath rule to the ruleset. Returns 1 on success, 0 on skip/failure.
    /// </summary>
    private static unsafe int AddPathRule(int rulesetFd, string path, ulong access, ILogger? logger)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            if (logger is not null)
            {
                LogPathNotExist(logger, path);
            }

            return 0;
        }

        int fd = OpenPath(path, OPath | OCloexec);
        if (fd < 0)
        {
            if (logger is not null)
            {
                LogOpenFailed(logger, path, Marshal.GetLastPInvokeError());
            }

            return 0;
        }

        try
        {
            var rule = new LandlockPathBeneathAttr
            {
                AllowedAccess = access,
                ParentFd = fd,
            };
            nint rulePtr = (nint)(&rule);
            long ret = SyscallAddRule(SysLandlockAddRule, rulesetFd,
                LandlockRulePathBeneath, rulePtr, 0L);
            if (ret != 0)
            {
                if (logger is not null)
                {
                    LogAddRuleFailed(logger, path, Marshal.GetLastPInvokeError());
                }

                return 0;
            }

            return 1;
        }
        finally
        {
            CloseFd(fd);
        }
    }

    // ── LoggerMessage source-generated methods ─────────────────────────────

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Landlock sandbox: disabled by config")]
    private static partial void LogSandboxDisabled(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug, Message = "Landlock sandbox: skipped (not Linux)")]
    private static partial void LogSandboxSkippedNotLinux(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Landlock sandbox: unexpected error; continuing without restrictions")]
    private static partial void LogSandboxUnexpectedError(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Landlock sandbox: unavailable ({Reason}); running unrestricted")]
    private static partial void LogSandboxUnavailable(ILogger logger, string reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Landlock sandbox: kernel ABI v{Abi} detected")]
    private static partial void LogSandboxAbiDetected(ILogger logger, int abi);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Landlock sandbox: landlock_create_ruleset failed (errno {Errno})")]
    private static partial void LogCreateRulesetFailed(ILogger logger, int errno);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Landlock sandbox: prctl(PR_SET_NO_NEW_PRIVS) failed (errno {Errno}); skipping restrict_self")]
    private static partial void LogPrctlFailed(ILogger logger, int errno);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning, Message = "Landlock sandbox: landlock_restrict_self failed (errno {Errno})")]
    private static partial void LogRestrictSelfFailed(ILogger logger, int errno);

    [LoggerMessage(EventId = 9, Level = LogLevel.Information, Message = "Landlock sandbox active: {Count} path rules applied (ABI v{Abi})")]
    private static partial void LogSandboxActive(ILogger logger, int count, int abi);

    [LoggerMessage(EventId = 10, Level = LogLevel.Debug, Message = "Landlock: {Path} does not exist; skipping rule")]
    private static partial void LogPathNotExist(ILogger logger, string path);

    [LoggerMessage(EventId = 11, Level = LogLevel.Debug, Message = "Landlock: open({Path}) failed (errno {Errno}); skipping")]
    private static partial void LogOpenFailed(ILogger logger, string path, int errno);

    [LoggerMessage(EventId = 12, Level = LogLevel.Debug, Message = "Landlock: add_rule({Path}) failed (errno {Errno})")]
    private static partial void LogAddRuleFailed(ILogger logger, string path, int errno);
}