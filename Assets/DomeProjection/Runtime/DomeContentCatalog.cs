using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DomeProjection
{
    [Serializable]
    public sealed class DomeMaterialCatalog
    {
        public int version = 1;
        public List<DomeMaterialDefinition> materials = new List<DomeMaterialDefinition>();

        public void Normalize()
        {
            materials ??= new List<DomeMaterialDefinition>();
            for (var i = materials.Count - 1; i >= 0; i--)
            {
                var item = materials[i];
                if (item == null)
                {
                    materials.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.id)) item.id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(item.name)) item.name = "名称未設定";
                item.projectionMode = Mathf.Clamp(item.projectionMode, 0, 2);
                item.durationSeconds = Mathf.Max(0f, item.durationSeconds);
            }
        }
    }

    [Serializable]
    public sealed class DomeMaterialDefinition
    {
        public string id = string.Empty;
        public string name = string.Empty;
        // Absolute paths are accepted. Relative paths are resolved from the
        // DomeProjectionConfig directory, making a portable show folder easy.
        public string path = string.Empty;
        // 0 = 2:1 equirectangular, 1 = flat, 2 = circular all-sky.
        public int projectionMode;
        public bool loop = true;
        public bool audioMuted;
        public bool flipInputX;
        public bool flipInputY;
        // Used to size playlist chapter segments. Older JSON files omit this
        // field and are updated automatically after the material is opened.
        public float durationSeconds;

        public float yaw;
        public float pitch = 180f;
        public float roll;

        public float allSkySourceCenterX = 0.5f;
        public float allSkySourceCenterY = 0.5f;
        public float allSkySourceRadius = 1f;
        public float allSkySourceRotation;
        public float allSkySourceRadialK1;
        public float allSkySourceRadialK2;

        public float flatAzimuth;
        public float flatPolarAngle = 45f;
        public float flatRoll = 180f;
        public float flatHorizontalFov = 100f;
        public float flatHeightScale = 1f;
        public float flatCropLeft;
        public float flatCropRight;
        public float flatCropTop;
        public float flatCropBottom;

        public static DomeMaterialDefinition Capture(string id, string name, string storedPath,
            DomeProjectionSettings settings, float durationSeconds = 0f)
        {
            return new DomeMaterialDefinition
            {
                id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                name = name,
                path = storedPath,
                projectionMode = settings.fileProjectionMode,
                loop = settings.loop,
                audioMuted = settings.audioMuted,
                flipInputX = settings.flipInputX,
                flipInputY = settings.flipInputY,
                durationSeconds = Mathf.Max(0f, durationSeconds),
                yaw = settings.yaw,
                pitch = settings.pitch,
                roll = settings.roll,
                allSkySourceCenterX = settings.allSkySourceCenterX,
                allSkySourceCenterY = settings.allSkySourceCenterY,
                allSkySourceRadius = settings.allSkySourceRadius,
                allSkySourceRotation = settings.allSkySourceRotation,
                allSkySourceRadialK1 = settings.allSkySourceRadialK1,
                allSkySourceRadialK2 = settings.allSkySourceRadialK2,
                flatAzimuth = settings.flatAzimuth,
                flatPolarAngle = settings.flatPolarAngle,
                flatRoll = settings.flatRoll,
                flatHorizontalFov = settings.flatHorizontalFov,
                flatHeightScale = settings.flatHeightScale,
                flatCropLeft = settings.flatCropLeft,
                flatCropRight = settings.flatCropRight,
                flatCropTop = settings.flatCropTop,
                flatCropBottom = settings.flatCropBottom
            };
        }

        public void ApplyTo(DomeProjectionSettings settings)
        {
            settings.fileProjectionMode = projectionMode;
            settings.fileIsFlat = projectionMode == 1;
            settings.loop = loop;
            settings.audioMuted = audioMuted;
            settings.flipInputX = flipInputX;
            settings.flipInputY = flipInputY;
            settings.yaw = yaw;
            settings.pitch = pitch;
            settings.roll = roll;
            settings.allSkySourceCenterX = allSkySourceCenterX;
            settings.allSkySourceCenterY = allSkySourceCenterY;
            settings.allSkySourceRadius = allSkySourceRadius;
            settings.allSkySourceRotation = allSkySourceRotation;
            settings.allSkySourceRadialK1 = allSkySourceRadialK1;
            settings.allSkySourceRadialK2 = allSkySourceRadialK2;
            settings.flatAzimuth = flatAzimuth;
            settings.flatPolarAngle = flatPolarAngle;
            settings.flatRoll = flatRoll;
            settings.flatHorizontalFov = flatHorizontalFov;
            settings.flatHeightScale = flatHeightScale;
            settings.flatCropLeft = flatCropLeft;
            settings.flatCropRight = flatCropRight;
            settings.flatCropTop = flatCropTop;
            settings.flatCropBottom = flatCropBottom;
            settings.Clamp();
        }
    }

    [Serializable]
    public sealed class DomePlaylistCatalog
    {
        public int version = 3;
        public List<DomePlaylistDefinition> playlists = new List<DomePlaylistDefinition>();

        public void Normalize()
        {
            var needsAutoAdvanceMigration = version < 2;
            var needsTransitionMigration = version < 3;
            playlists ??= new List<DomePlaylistDefinition>();
            for (var i = playlists.Count - 1; i >= 0; i--)
            {
                var playlist = playlists[i];
                if (playlist == null)
                {
                    playlists.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(playlist.id)) playlist.id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(playlist.name)) playlist.name = "名称未設定";
                if (needsAutoAdvanceMigration) playlist.autoAdvance = true;
                if (needsTransitionMigration)
                {
                    playlist.transitionMode = 1;
                    playlist.transitionSeconds = 0.8f;
                }
                playlist.transitionMode = Mathf.Clamp(playlist.transitionMode, 0, 1);
                playlist.transitionSeconds = Mathf.Clamp(playlist.transitionSeconds, 0.2f, 3f);
                playlist.materialIds ??= new List<string>();
            }
            version = 3;
        }
    }

    [Serializable]
    public sealed class DomePlaylistDefinition
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public bool autoAdvance = true;
        public bool loop;
        public int transitionMode = 1;
        public float transitionSeconds = 0.8f;
        public List<string> materialIds = new List<string>();
    }

    public sealed class DomeConfigurationStore
    {
        public const string MaterialsFileName = "materials.json";
        public const string PlaylistsFileName = "playlists.json";
        public const string ReadmeFileName = "README.txt";

        public string DirectoryPath { get; }
        public string MaterialsPath => Path.Combine(DirectoryPath, MaterialsFileName);
        public string PlaylistsPath => Path.Combine(DirectoryPath, PlaylistsFileName);
        public string ReadmePath => Path.Combine(DirectoryPath, ReadmeFileName);
        public string LensDirectoryPath => Path.Combine(DirectoryPath, "Lenses");

        public DomeConfigurationStore(string directoryPath)
        {
            DirectoryPath = directoryPath;
            Directory.CreateDirectory(DirectoryPath);
            Directory.CreateDirectory(LensDirectoryPath);
            EnsureReadme();
        }

        private void EnsureReadme()
        {
            if (File.Exists(ReadmePath)) return;

            File.WriteAllText(ReadmePath,
                "Dome Projection JSON settings\n"
                + "=============================\n\n"
                + "The Control app loads these files automatically at startup.\n"
                + "Use the Reload JSON button on tab 5 to apply edits while the app is running.\n"
                + "Save JSON as UTF-8. Relative media paths are based on this folder.\n\n"
                + "materials.json\n"
                + "- materials: registered videos/images and their projection parameters\n"
                + "- id: unique non-empty string used by playlists\n"
                + "- path: absolute path or a path relative to this folder\n"
                + "- projectionMode: 0 = 360 equirectangular, 1 = flat, 2 = circular all-sky\n"
                + "- durationSeconds: video length used for proportional playlist chapter widths\n"
                + "- loop/audioMuted/flipInputX/flipInputY: true or false\n"
                + "- yaw/pitch/roll and the remaining values are the same as the Control UI\n\n"
                + "playlists.json\n"
                + "- playlists: ordered playback lists\n"
                + "- materialIds: material id values from materials.json in playback order\n"
                + "- autoAdvance: automatically open the next item when a video ends\n"
                + "- loop: return to the first item after the last item\n\n"
                + "- transitionMode: 0 = cut, 1 = fade through black\n"
                + "- transitionSeconds: total fade duration in seconds\n\n"
                + "Lenses/*.json\n"
                + "- one saved lens calibration per JSON file\n\n"
                + "Do not edit a file while the app is saving that same list.\n");
        }

        public DomeMaterialCatalog LoadMaterials()
        {
            var catalog = File.Exists(MaterialsPath)
                ? JsonUtility.FromJson<DomeMaterialCatalog>(File.ReadAllText(MaterialsPath))
                : new DomeMaterialCatalog();
            catalog ??= new DomeMaterialCatalog();
            catalog.Normalize();
            if (!File.Exists(MaterialsPath)) SaveMaterials(catalog);
            return catalog;
        }

        public DomePlaylistCatalog LoadPlaylists()
        {
            var fileExists = File.Exists(PlaylistsPath);
            var catalog = fileExists
                ? JsonUtility.FromJson<DomePlaylistCatalog>(File.ReadAllText(PlaylistsPath))
                : new DomePlaylistCatalog();
            catalog ??= new DomePlaylistCatalog();
            var loadedVersion = catalog.version;
            catalog.Normalize();
            if (!fileExists || loadedVersion < 3) SavePlaylists(catalog);
            return catalog;
        }

        public void SaveMaterials(DomeMaterialCatalog catalog)
        {
            catalog.Normalize();
            File.WriteAllText(MaterialsPath, JsonUtility.ToJson(catalog, true));
        }

        public void SavePlaylists(DomePlaylistCatalog catalog)
        {
            catalog.Normalize();
            File.WriteAllText(PlaylistsPath, JsonUtility.ToJson(catalog, true));
        }

        public string ResolveMediaPath(string configuredPath)
        {
            var path = (configuredPath ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path) || path.Contains("://")) return path;
            if (!Path.IsPathRooted(path)) path = Path.Combine(DirectoryPath, path);
            return Path.GetFullPath(path);
        }

        public string MakePortableMediaPath(string mediaPath)
        {
            var path = (mediaPath ?? string.Empty).Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path) || path.Contains("://")) return path;
            try
            {
                return Path.GetRelativePath(DirectoryPath, Path.GetFullPath(path))
                    .Replace(Path.DirectorySeparatorChar, '/');
            }
            catch
            {
                return path;
            }
        }
    }
}
