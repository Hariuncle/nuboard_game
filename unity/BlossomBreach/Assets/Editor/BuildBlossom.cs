using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlossomBreach
{
public static class BuildBlossom
{
    private const string ScenePath = "Assets/Scenes/BlossomBreach.unity";
    private const string ExecutablePath = "Builds/Windows/BlossomBreach.exe";

    [MenuItem("Blossom Breach/Build Windows", priority = 1)]
    public static void BuildWindows()
    {
        ConfigurePlayer();
        CopyOptionalIntro();
        CreateBuildScene();

        string outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ExecutablePath));
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Blossom Breach build failed: {report.summary.result} " +
                $"({report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings)");
        }

        Debug.Log($"Blossom Breach Windows build created at {outputPath} ({report.summary.totalSize:N0} bytes).");
    }

    [MenuItem("Blossom Breach/Create Build Scene", priority = 0)]
    public static void CreateBuildScene()
    {
        EnsureFolder("Assets", "Scenes");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var bootstrap = new GameObject("Game Bootstrap");
        bootstrap.AddComponent<GameBootstrap>();

        if (!EditorSceneManager.SaveScene(scene, ScenePath, true))
        {
            throw new InvalidOperationException($"Could not save build scene to {ScenePath}.");
        }

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        AssetDatabase.SaveAssets();
    }

    private static void ConfigurePlayer()
    {
        PlayerSettings.productName = "Blossom Breach";
        PlayerSettings.companyName = "Blossom Breach";
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.blossombreach.game");
    }

    private static void CopyOptionalIntro()
    {
        string source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "game", "assets", "video", "h3-meadow-intro.mp4"));
        if (!File.Exists(source))
        {
            Debug.Log("Optional intro movie was not found; building without it.");
            return;
        }

        string streamingAssets = Path.Combine(Application.dataPath, "StreamingAssets");
        Directory.CreateDirectory(streamingAssets);
        string destination = Path.Combine(streamingAssets, "h3-meadow-intro.mp4");
        File.Copy(source, destination, true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
}
