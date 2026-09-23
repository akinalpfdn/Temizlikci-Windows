namespace Temizlikci.Domain.Cleanup;

/// <summary>
/// The built-in rules. Each label follows the owning tool's or Windows' own documentation (checked 2026-09-23); where a
/// folder looks like a cache but isn't one, the label says so. The reason text for each ID is in the presentation layer.
/// </summary>
public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupRule> Rules { get; } =
    [
        // Windows — its own space users. Folders Windows needs are never offered for the Recycle Bin.
        new("windows.updateDownloads", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtPath(@"%WINDIR%\SoftwareDistribution\Download"), CleanupAction.OpenStorageSettings),
        new("windows.componentStore", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtPath(@"%WINDIR%\WinSxS"), CleanupAction.CleanUpComponents),
        new("windows.installer", Ecosystem.Windows, SafetyLevel.Keep, RuleMatcher.AtPath(@"%WINDIR%\Installer"), CleanupAction.None),
        new("windows.temp", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtPath(@"%WINDIR%\Temp"), CleanupAction.OpenStorageSettings),
        new("windows.userTemp", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Temp"), CleanupAction.OpenStorageSettings),
        new("windows.deliveryOptimization", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtPath(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization"), CleanupAction.OpenStorageSettings),
        new("windows.errorReports", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtPath(@"%PROGRAMDATA%\Microsoft\Windows\WER"), CleanupAction.OpenStorageSettings),
        new("windows.crashDumps", Ecosystem.Windows, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\CrashDumps"), CleanupAction.MoveToRecycleBin),
        new("windows.packageCache", Ecosystem.Windows, SafetyLevel.Keep, RuleMatcher.AtPath(@"%PROGRAMDATA%\Package Cache"), CleanupAction.None),
        new("windows.previousInstallation", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtDriveRoot("Windows.old"), CleanupAction.OpenStorageSettings),
        new("windows.recycleBin", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtDriveRoot("$Recycle.Bin"), CleanupAction.OpenRecycleBin),
        new("windows.systemVolumeInformation", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtDriveRoot("System Volume Information"), CleanupAction.OpenSystemProtection),
        new("windows.hibernationFile", Ecosystem.Windows, SafetyLevel.Tool, RuleMatcher.AtDriveRoot("hiberfil.sys"), CleanupAction.None),
        new("windows.pageFile", Ecosystem.Windows, SafetyLevel.Keep, RuleMatcher.AtDriveRoot("pagefile.sys"), CleanupAction.None),
        new("windows.swapFile", Ecosystem.Windows, SafetyLevel.Keep, RuleMatcher.AtDriveRoot("swapfile.sys"), CleanupAction.None),

        // Visual Studio
        new("vs.solutionCache", Ecosystem.VisualStudio, SafetyLevel.Safe, RuleMatcher.ProjectFolder(".vs", "*.sln"), CleanupAction.MoveToRecycleBin),
        new("vs.solutionCacheSlnx", Ecosystem.VisualStudio, SafetyLevel.Safe, RuleMatcher.ProjectFolder(".vs", "*.slnx"), CleanupAction.MoveToRecycleBin),
        new("vs.installerCache", Ecosystem.VisualStudio, SafetyLevel.Tool, RuleMatcher.AtPath(@"%PROGRAMDATA%\Microsoft\VisualStudio\Packages"), CleanupAction.None),

        // .NET
        new("dotnet.packages", Ecosystem.DotNet, SafetyLevel.Tool, RuleMatcher.AtPath(@"~\.nuget\packages"), CleanupAction.None),
        new("dotnet.httpCache", Ecosystem.DotNet, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\NuGet\v3-cache"), CleanupAction.MoveToRecycleBin),
        new("dotnet.bin", Ecosystem.DotNet, SafetyLevel.Safe, RuleMatcher.ProjectFolder("bin", "*.csproj"), CleanupAction.MoveToRecycleBin),
        new("dotnet.obj", Ecosystem.DotNet, SafetyLevel.Safe, RuleMatcher.ProjectFolder("obj", "*.csproj"), CleanupAction.MoveToRecycleBin),

        // WSL — every distribution is a virtual disk; removing it by hand loses the whole Linux installation.
        new("wsl.distributions", Ecosystem.Wsl, SafetyLevel.Tool, RuleMatcher.AtPath(@"%LOCALAPPDATA%\wsl"), CleanupAction.ManageWsl),
        new("wsl.ubuntu", Ecosystem.Wsl, SafetyLevel.Tool, RuleMatcher.ChildOf(@"%LOCALAPPDATA%\Packages", "CanonicalGroupLimited."), CleanupAction.ManageWsl),
        new("wsl.debian", Ecosystem.Wsl, SafetyLevel.Tool, RuleMatcher.ChildOf(@"%LOCALAPPDATA%\Packages", "TheDebianProject."), CleanupAction.ManageWsl),

        // Docker
        new("docker.data", Ecosystem.Docker, SafetyLevel.Tool, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Docker\wsl"), CleanupAction.None),

        // Android
        new("android.gradleCaches", Ecosystem.Android, SafetyLevel.Safe, RuleMatcher.AtPath(@"~\.gradle\caches"), CleanupAction.MoveToRecycleBin),
        new("android.gradleWrapper", Ecosystem.Android, SafetyLevel.Safe, RuleMatcher.AtPath(@"~\.gradle\wrapper\dists"), CleanupAction.MoveToRecycleBin),
        new("android.emulators", Ecosystem.Android, SafetyLevel.Tool, RuleMatcher.AtPath(@"~\.android\avd"), CleanupAction.OpenAndroidStudio),
        new("android.systemImages", Ecosystem.Android, SafetyLevel.Tool, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Android\Sdk\system-images"), CleanupAction.OpenAndroidStudio),
        new("android.studioCaches", Ecosystem.Android, SafetyLevel.Safe, RuleMatcher.ChildOf(@"%LOCALAPPDATA%\Google", "AndroidStudio"), CleanupAction.MoveToRecycleBin),

        // Flutter & Dart
        new("flutter.build", Ecosystem.Flutter, SafetyLevel.Safe, RuleMatcher.ProjectFolder("build", "pubspec.yaml"), CleanupAction.MoveToRecycleBin),
        new("flutter.dartTool", Ecosystem.Flutter, SafetyLevel.Safe, RuleMatcher.ProjectFolder(".dart_tool", "pubspec.yaml"), CleanupAction.MoveToRecycleBin),
        new("flutter.pubCache", Ecosystem.Flutter, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Pub\Cache"), CleanupAction.MoveToRecycleBin),

        // Node.js
        new("node.modules", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.ProjectFolder("node_modules", "package.json"), CleanupAction.MoveToRecycleBin),
        new("node.next", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.ProjectFolder(".next", "package.json"), CleanupAction.MoveToRecycleBin),
        new("node.npmCache", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\npm-cache\_cacache"), CleanupAction.MoveToRecycleBin),
        new("node.yarnCache", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Yarn\Cache"), CleanupAction.MoveToRecycleBin),
        new("node.yarnBerryCache", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Yarn\Berry\cache"), CleanupAction.MoveToRecycleBin),
        new("node.pnpmStore", Ecosystem.Node, SafetyLevel.Tool, RuleMatcher.AtPath(@"%LOCALAPPDATA%\pnpm\store"), CleanupAction.None),
        new("node.bunCache", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"~\.bun\install\cache"), CleanupAction.MoveToRecycleBin),
        new("node.electronCache", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\electron\Cache"), CleanupAction.MoveToRecycleBin),
        new("node.gypCache", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\node-gyp\Cache"), CleanupAction.MoveToRecycleBin),
        new("node.playwright", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\ms-playwright"), CleanupAction.MoveToRecycleBin),
        new("node.cypress", Ecosystem.Node, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Cypress\Cache"), CleanupAction.MoveToRecycleBin),

        // Rust
        new("rust.registry", Ecosystem.Rust, SafetyLevel.Safe, RuleMatcher.AtPath(@"~\.cargo\registry"), CleanupAction.MoveToRecycleBin),
        new("rust.target", Ecosystem.Rust, SafetyLevel.Safe, RuleMatcher.ProjectFolder("target", "Cargo.toml"), CleanupAction.MoveToRecycleBin),
        new("rust.toolchains", Ecosystem.Rust, SafetyLevel.Tool, RuleMatcher.AtPath(@"~\.rustup"), CleanupAction.None),

        // Go
        new("go.buildCache", Ecosystem.Go, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\go-build"), CleanupAction.MoveToRecycleBin),
        new("go.modCache", Ecosystem.Go, SafetyLevel.Tool, RuleMatcher.AtPath(@"~\go\pkg\mod"), CleanupAction.None),

        // Python
        new("python.pipCache", Ecosystem.Python, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\pip\Cache"), CleanupAction.MoveToRecycleBin),
        new("python.uvCache", Ecosystem.Python, SafetyLevel.Tool, RuleMatcher.AtPath(@"%LOCALAPPDATA%\uv\cache"), CleanupAction.None),

        // Java
        new("java.maven", Ecosystem.Java, SafetyLevel.Safe, RuleMatcher.AtPath(@"~\.m2\repository"), CleanupAction.MoveToRecycleBin),
        new("java.gradleBuild", Ecosystem.Java, SafetyLevel.Safe, RuleMatcher.ProjectFolder("build", "build.gradle"), CleanupAction.MoveToRecycleBin),
        new("java.gradleBuildKts", Ecosystem.Java, SafetyLevel.Safe, RuleMatcher.ProjectFolder("build", "build.gradle.kts"), CleanupAction.MoveToRecycleBin),
        new("java.mavenTarget", Ecosystem.Java, SafetyLevel.Safe, RuleMatcher.ProjectFolder("target", "pom.xml"), CleanupAction.MoveToRecycleBin),

        // Unity
        new("unity.library", Ecosystem.Unity, SafetyLevel.Safe, RuleMatcher.ProjectFolder("Library", "ProjectSettings"), CleanupAction.MoveToRecycleBin),
        new("unity.cache", Ecosystem.Unity, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Unity\cache"), CleanupAction.MoveToRecycleBin),

        // Editors
        new("editors.jetbrains", Ecosystem.Editors, SafetyLevel.Safe, RuleMatcher.AtPath(@"%LOCALAPPDATA%\JetBrains"), CleanupAction.MoveToRecycleBin),
        new("editors.vscodeCache", Ecosystem.Editors, SafetyLevel.Safe, RuleMatcher.AtPath(@"%APPDATA%\Code\Cache"), CleanupAction.MoveToRecycleBin),
        new("editors.vscodeCachedData", Ecosystem.Editors, SafetyLevel.Safe, RuleMatcher.AtPath(@"%APPDATA%\Code\CachedData"), CleanupAction.MoveToRecycleBin),
        new("editors.vscodeExtensionDownloads", Ecosystem.Editors, SafetyLevel.Safe, RuleMatcher.AtPath(@"%APPDATA%\Code\CachedExtensionVSIXs"), CleanupAction.MoveToRecycleBin),

        // App data — kept; the search continues inside, because WSL distributions live in Store app folders.
        new("appData.packages", Ecosystem.AppData, SafetyLevel.Keep, RuleMatcher.AtPath(@"%LOCALAPPDATA%\Packages"), CleanupAction.None),
    ];
}
