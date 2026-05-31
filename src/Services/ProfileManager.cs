using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AnalogtoKey.Models;

namespace AnalogtoKey.Services
{
    public class ProfileManager
    {
        private static readonly string ProfileDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnalogtoKey", "profiles");

        private static readonly string SettingsPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnalogtoKey", "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        // ─── Indstillinger ────────────────────────────────────────

        private class AppSettings
        {
            public string  LastProfile  { get; set; } = "Default";
            public double? WindowLeft   { get; set; }
            public double? WindowTop    { get; set; }
            public double? WindowWidth  { get; set; }
            public double? WindowHeight { get; set; }
        }

        private AppSettings LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                       ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        private void SaveSettings(AppSettings s) =>
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, JsonOptions));

        public string LoadLastProfile() => LoadSettings().LastProfile;

        public void SaveLastProfile(string name)
        {
            var s = LoadSettings();
            s.LastProfile = name;
            SaveSettings(s);
        }

        public (double Left, double Top, double Width, double Height)? LoadWindowState()
        {
            var s = LoadSettings();
            if (s.WindowLeft == null || s.WindowTop == null ||
                s.WindowWidth == null || s.WindowHeight == null)
                return null;
            return (s.WindowLeft.Value, s.WindowTop.Value, s.WindowWidth.Value, s.WindowHeight.Value);
        }

        public void SaveWindowState(double left, double top, double width, double height)
        {
            var s = LoadSettings();
            s.WindowLeft   = left;
            s.WindowTop    = top;
            s.WindowWidth  = width;
            s.WindowHeight = height;
            SaveSettings(s);
        }

        // ─── Profiler ─────────────────────────────────────────────

        public ProfileManager()
        {
            Directory.CreateDirectory(ProfileDir);
            if (!File.Exists(GetPath("Default")))
                Save(new MappingProfile { Name = "Default" });
        }

        public List<string> ListProfiles()
        {
            var names = new List<string>();
            foreach (var f in Directory.GetFiles(ProfileDir, "*.json"))
                names.Add(Path.GetFileNameWithoutExtension(f));
            return names;
        }

        public MappingProfile Load(string name)
        {
            var path = GetPath(name);
            if (!File.Exists(path)) return new MappingProfile { Name = name };
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MappingProfile>(json, JsonOptions)
                   ?? new MappingProfile { Name = name };
        }

        public void Save(MappingProfile profile) =>
            File.WriteAllText(GetPath(profile.Name), JsonSerializer.Serialize(profile, JsonOptions));

        public void Delete(string name)
        {
            var path = GetPath(name);
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>Omdøber profil. Returnerer false hvis nyt navn er tomt eller allerede eksisterer.</summary>
        public bool Rename(string oldName, string newName)
        {
            newName = newName.Trim();
            if (string.IsNullOrEmpty(newName)) return false;
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return true;
            if (File.Exists(GetPath(newName))) return false;

            var profile = Load(oldName);
            profile.Name = newName;
            Save(profile);
            File.Delete(GetPath(oldName));
            return true;
        }

        private static string GetPath(string name) =>
            Path.Combine(ProfileDir, $"{name}.json");
    }
}
