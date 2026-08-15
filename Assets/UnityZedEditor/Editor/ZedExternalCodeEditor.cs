using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Unity.VisualStudio.Editor;
using Unity.CodeEditor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityZedEditor
{
    [InitializeOnLoad]
    internal sealed class ZedExternalCodeEditor : IExternalCodeEditor
    {
        const string EditorName = "Zed";
        static readonly IGenerator ProjectGenerator = CreateProjectGenerator();
        static readonly Dictionary<string, string> VersionCache = new(
            StringComparer.OrdinalIgnoreCase
        );
        static readonly Dictionary<string, Task<string>> PendingVersionReads = new(
            StringComparer.OrdinalIgnoreCase
        );
        const string ZedSettings = @"{
  ""file_scan_exclusions"": [
    ""**/.git"",
    ""**/.svn"",
    ""**/.hg"",
    ""**/.jj"",
    ""**/CVS"",
    ""**/.DS_Store"",
    ""**/Thumbs.db"",
    ""**/.classpath"",
    ""**/.settings"",
    ""**/.vs"",
    ""**/Library"",
    ""**/library"",
    ""**/Temp"",
    ""**/temp"",
    ""**/Obj"",
    ""**/obj"",
    ""**/Logs"",
    ""**/logs"",
    ""**/UserSettings""
  ],
  ""file_types"": {
    ""YAML"": [
      ""*.asset"",
      ""*.meta"",
      ""*.prefab"",
      ""*.unity"",
      ""*.mat"",
      ""*.anim"",
      ""*.controller"",
      ""*.overrideController"",
      ""*.playable"",
      ""*.mask""
    ]
  }
}
";
        const string ZedDebugConfiguration = @"[
  {
    ""label"": ""Attach to Unity"",
    ""adapter"": ""monodbg"",
    ""request"": ""attach"",
    ""processId"": 0
  }
]
";

        static ZedExternalCodeEditor()
        {
            CodeEditor.Register(new ZedExternalCodeEditor());
        }

        public CodeEditor.Installation[] Installations => DiscoverInstallations().ToArray();

        public bool TryGetInstallationForPath(
            string editorPath,
            out CodeEditor.Installation installation
        )
        {
            if (IsZedExecutable(editorPath))
            {
                installation = CreateInstallation(editorPath);
                return true;
            }

            installation = default;
            return false;
        }

        public void Initialize(string editorInstallationPath)
        {
            if (IsZedExecutable(editorInstallationPath))
                EnsureZedSettings();
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Generate .csproj files for:");
            EditorGUI.indentLevel++;
            SettingsButton(ProjectGenerationFlag.Embedded, "Embedded packages", "");
            SettingsButton(ProjectGenerationFlag.Local, "Local packages", "");
            SettingsButton(ProjectGenerationFlag.Registry, "Registry packages", "");
            SettingsButton(ProjectGenerationFlag.Git, "Git packages", "");
            SettingsButton(ProjectGenerationFlag.BuiltIn, "Built-in packages", "");
            SettingsButton(ProjectGenerationFlag.LocalTarBall, "Local tarball", "");
            SettingsButton(ProjectGenerationFlag.Unknown, "Packages from unknown sources", "");
            SettingsButton(
                ProjectGenerationFlag.PlayerAssemblies,
                "Player projects",
                "For each player project generate an additional csproj with the name 'project-player.csproj'"
            );
            RegenerateProjectFiles();
            EditorGUI.indentLevel--;
        }

        public void SyncAll()
        {
            EnsureZedSettings();
            ProjectGenerator.Sync();
        }

        public void SyncIfNeeded(
            string[] addedFiles,
            string[] deletedFiles,
            string[] movedFiles,
            string[] movedFromFiles,
            string[] importedFiles
        )
        {
            EnsureZedSettings();
            ProjectGenerator.SyncIfNeeded(
                addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles),
                importedFiles
            );
        }

        public bool OpenProject(string filePath, int line, int column)
        {
            var editorPath = CodeEditor.CurrentEditorInstallation;
            if (!TryGetInstallationForPath(editorPath, out _))
            {
                Debug.LogWarning(
                    $"Zed executable was not found at '{editorPath}'. "
                        + "Select Zed again in Preferences > External Tools."
                );
                return false;
            }

            if (!string.IsNullOrEmpty(filePath) && !ProjectGenerator.IsSupportedFile(filePath))
                return false;

            try
            {
                EnsureZedSettings();

                if (!ProjectGenerator.HasSolutionBeenGenerated())
                    ProjectGenerator.Sync();

                var projectDirectory = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectDirectory))
                    return false;

                var arguments = new List<string> { projectDirectory };
                if (!string.IsNullOrEmpty(filePath))
                {
                    var absolutePath = Path.GetFullPath(filePath);
                    arguments.Add(WithLocation(absolutePath, line, column));
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = editorPath,
                    Arguments = string.Join(" ", arguments.Select(CodeEditor.QuoteForProcessStart)),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = projectDirectory,
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not open Zed: {exception.Message}");
                return false;
            }
        }

        static IGenerator CreateProjectGenerator()
        {
            // HACK: The official package keeps its SDK-style generator internal.
            // Use that implementation by reflection.
            var sdkGeneratorType = typeof(ProjectGeneration).Assembly.GetType(
                "Microsoft.Unity.VisualStudio.Editor.SdkStyleProjectGeneration"
            );

            if (sdkGeneratorType != null)
            {
                try
                {
                    return (IGenerator)Activator.CreateInstance(sdkGeneratorType, true);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"Could not create Unity's SDK-style project generator; using legacy generation. {exception.Message}"
                    );
                }
            }

            return new ProjectGeneration();
        }

        static void SettingsButton(ProjectGenerationFlag preference, string label, string tooltip)
        {
            var previousValue = ProjectGenerator.AssemblyNameProvider.ProjectGenerationFlag.HasFlag(
                preference
            );
            var newValue = EditorGUILayout.Toggle(new GUIContent(label, tooltip), previousValue);
            if (newValue != previousValue)
                ProjectGenerator.AssemblyNameProvider.ToggleProjectGeneration(preference);
        }

        static void RegenerateProjectFiles()
        {
            var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect());
            rect.width = 252;
            if (GUI.Button(rect, "Regenerate project files"))
            {
                EnsureZedSettings();
                ProjectGenerator.Sync();
            }
        }

        static void EnsureZedSettings()
        {
            try
            {
                var projectDirectory = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectDirectory))
                    return;

                var zedDirectory = Path.Combine(projectDirectory, ".zed");
                var settingsPath = Path.Combine(zedDirectory, "settings.json");
                var debugConfigurationPath = Path.Combine(zedDirectory, "debug.json");

                Directory.CreateDirectory(zedDirectory);
                if (!File.Exists(settingsPath))
                    File.WriteAllText(settingsPath, ZedSettings, new UTF8Encoding(false));

                if (!File.Exists(debugConfigurationPath))
                {
                    File.WriteAllText(
                        debugConfigurationPath,
                        ZedDebugConfiguration,
                        new UTF8Encoding(false)
                    );
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not create Zed project settings: {exception.Message}");
            }
        }

        static IEnumerable<CodeEditor.Installation> DiscoverInstallations()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in CandidatePaths())
            {
                if (
                    !string.IsNullOrEmpty(path)
                    && File.Exists(path)
                    && seen.Add(Path.GetFullPath(path))
                )
                    yield return CreateInstallation(path);
            }
        }

        static IEnumerable<string> CandidatePaths()
        {
            switch (SystemInfo.operatingSystemFamily)
            {
                case OperatingSystemFamily.MacOSX:
                    yield return "/Applications/Zed.app/Contents/MacOS/cli";
                    yield return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Applications/Zed.app/Contents/MacOS/cli"
                    );
                    break;
                case OperatingSystemFamily.Windows:
                    yield return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs",
                        "Zed",
                        "Zed.exe"
                    );
                    yield return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Zed",
                        "Zed.exe"
                    );
                    break;
                case OperatingSystemFamily.Linux:
                    yield return "/usr/bin/zed";
                    yield return "/usr/bin/zeditor";
                    yield return "/usr/local/bin/zed";
                    yield return "/var/lib/flatpak/app/dev.zed.Zed/current/active/files/bin/zed";
                    yield return "/run/current-system/sw/bin/zed";
                    yield return "/run/current-system/sw/bin/zeditor";

                    var userName = Environment.UserName;
                    if (!string.IsNullOrEmpty(userName))
                    {
                        var userProfileBin = Path.Combine(
                            "/etc/profiles/per-user",
                            userName,
                            "bin"
                        );
                        yield return Path.Combine(userProfileBin, "zed");
                        yield return Path.Combine(userProfileBin, "zeditor");
                    }

                    yield return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".local",
                        "bin",
                        "zed"
                    );
                    break;
            }
        }

        static CodeEditor.Installation CreateInstallation(string path)
        {
            var absolutePath = Path.GetFullPath(path);
            var version = GetZedVersion(absolutePath);
            return new CodeEditor.Installation
            {
                Name = string.IsNullOrEmpty(version) ? EditorName : $"{EditorName} [{version}]",
                Path = absolutePath,
            };
        }

        static string GetZedVersion(string path)
        {
            if (VersionCache.TryGetValue(path, out var cachedVersion))
                return cachedVersion;

            if (!PendingVersionReads.ContainsKey(path))
            {
                PendingVersionReads[path] = Task.Run(() => ReadZedVersion(path));
                EditorApplication.update -= CompleteVersionReads;
                EditorApplication.update += CompleteVersionReads;
            }

            return null;
        }

        static void CompleteVersionReads()
        {
            var completedPaths = PendingVersionReads
                .Where(pair => pair.Value.IsCompleted)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var path in completedPaths)
            {
                var task = PendingVersionReads[path];
                PendingVersionReads.Remove(path);

                try
                {
                    VersionCache[path] = task.GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    VersionCache[path] = null;
                }
            }

            if (completedPaths.Length > 0)
                InternalEditorUtility.RepaintAllViews();

            if (PendingVersionReads.Count == 0)
                EditorApplication.update -= CompleteVersionReads;
        }

        static string ReadZedVersion(string path)
        {
            try
            {
                using (
                    var process = Process.Start(
                        new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "--version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        }
                    )
                )
                {
                    if (process == null)
                        return null;

                    if (!process.WaitForExit(2000))
                    {
                        process.Kill();
                        return null;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    var match = Regex.Match(
                        output + " " + error,
                        @"\b\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?\b"
                    );
                    if (match.Success)
                        return match.Value;
                }
            }
            catch (Exception)
            {
                // A version is optional; discovery and launching still work without it.
            }

            return null;
        }

        static bool IsZedExecutable(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            var name = Path.GetFileNameWithoutExtension(path);
            if (
                string.Equals(name, "zed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "zeditor", StringComparison.OrdinalIgnoreCase)
            )
                return true;

            return string.Equals(name, "cli", StringComparison.OrdinalIgnoreCase)
                && path.IndexOf("Zed.app", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string WithLocation(string path, int line, int column)
        {
            if (line <= 0)
                return path;

            return column > 0 ? $"{path}:{line}:{column}" : $"{path}:{line}";
        }
    }
}
