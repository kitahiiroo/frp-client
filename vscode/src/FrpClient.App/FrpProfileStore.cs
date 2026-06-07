using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FrpClient.App;

public class FrpProfileStore
{
    private const string ConfigFileName = "frpc.toml";
    private const string StoreFileName = "profiles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _profilesDirectory;
    private readonly string _storePath;
    private readonly string _templateConfigPath;

    private FrpProfileStore(string profilesDirectory, string templateConfigPath)
    {
        _profilesDirectory = profilesDirectory;
        _storePath = Path.Combine(profilesDirectory, StoreFileName);
        _templateConfigPath = templateConfigPath;
    }

    public string ActiveProfileId { get; private set; } = "default";

    public ObservableCollection<FrpProfileInfo> Profiles { get; } = [];

    public static FrpProfileStore Load(string profilesDirectory, string templateConfigPath)
    {
        var store = new FrpProfileStore(profilesDirectory, templateConfigPath);
        Directory.CreateDirectory(profilesDirectory);

        if (!File.Exists(store._storePath))
        {
            var defaultProfile = new FrpProfileInfo
            {
                Id = "default",
                Name = "默认配置",
                CreatedAt = DateTimeOffset.Now
            };

            store.Profiles.Add(defaultProfile);
            store.ActiveProfileId = defaultProfile.Id;
            store.EnsureProfileConfigFile(defaultProfile);
            store.Save();
            return store;
        }

        var document = ReadStoreDocument(store._storePath);

        foreach (var profile in document.Profiles.Where(profile =>
                     !string.IsNullOrWhiteSpace(profile.Id) &&
                     !string.IsNullOrWhiteSpace(profile.Name)))
        {
            store.Profiles.Add(profile);
        }

        if (store.Profiles.Count == 0)
        {
            store.Profiles.Add(new FrpProfileInfo
            {
                Id = "default",
                Name = "默认配置",
                CreatedAt = DateTimeOffset.Now
            });
        }

        store.ActiveProfileId = store.Profiles.Any(profile => profile.Id == document.ActiveProfileId)
            ? document.ActiveProfileId
            : store.Profiles[0].Id;

        foreach (var profile in store.Profiles)
        {
            store.EnsureProfileConfigFile(profile);
        }

        store.Save();
        return store;
    }

    public FrpProfileInfo GetActiveProfile()
    {
        return Profiles.FirstOrDefault(profile => profile.Id == ActiveProfileId) ?? Profiles[0];
    }

    public void SetActiveProfile(FrpProfileInfo profile)
    {
        ActiveProfileId = profile.Id;
        Save();
    }

    public FrpProfileInfo CreateProfile(string name, FrpProfileInfo? sourceProfile)
    {
        var profile = new FrpProfileInfo
        {
            Id = CreateUniqueProfileId(name),
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.Now
        };

        Profiles.Add(profile);
        Directory.CreateDirectory(GetProfileDirectory(profile));

        var configPath = GetConfigPath(profile);
        var sourceConfigPath = sourceProfile is null ? _templateConfigPath : GetConfigPath(sourceProfile);

        if (File.Exists(sourceConfigPath))
        {
            File.Copy(sourceConfigPath, configPath, overwrite: true);
        }
        else
        {
            File.WriteAllText(configPath, CreateDefaultConfigText());
        }

        ActiveProfileId = profile.Id;
        Save();
        return profile;
    }

    public void RenameProfile(FrpProfileInfo profile, string name)
    {
        profile.Name = name.Trim();
        Save();
    }

    public FrpProfileInfo DeleteProfile(FrpProfileInfo profile)
    {
        if (Profiles.Count <= 1)
        {
            throw new InvalidOperationException("至少需要保留一个配置。");
        }

        var removedIndex = Profiles.IndexOf(profile);
        Profiles.Remove(profile);

        var profileDirectory = GetProfileDirectory(profile);

        if (Directory.Exists(profileDirectory))
        {
            Directory.Delete(profileDirectory, recursive: true);
        }

        if (ActiveProfileId == profile.Id)
        {
            ActiveProfileId = Profiles[Math.Clamp(removedIndex, 0, Profiles.Count - 1)].Id;
        }

        Save();
        return GetActiveProfile();
    }

    public string GetConfigPath(FrpProfileInfo profile)
    {
        return Path.Combine(GetProfileDirectory(profile), ConfigFileName);
    }

    public void EnsureProfileConfigFile(FrpProfileInfo profile)
    {
        var profileDirectory = GetProfileDirectory(profile);
        Directory.CreateDirectory(profileDirectory);

        var configPath = GetConfigPath(profile);

        if (File.Exists(configPath))
        {
            return;
        }

        if (File.Exists(_templateConfigPath))
        {
            File.Copy(_templateConfigPath, configPath, overwrite: false);
            return;
        }

        File.WriteAllText(configPath, CreateDefaultConfigText());
    }

    public void Save()
    {
        Directory.CreateDirectory(_profilesDirectory);

        var document = new FrpProfileStoreDocument
        {
            ActiveProfileId = ActiveProfileId,
            Profiles = Profiles.ToList()
        };

        File.WriteAllText(_storePath, JsonSerializer.Serialize(document, JsonOptions));
    }

    private string GetProfileDirectory(FrpProfileInfo profile)
    {
        return Path.Combine(_profilesDirectory, profile.Id);
    }

    private string CreateUniqueProfileId(string name)
    {
        var baseId = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');

        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "profile";
        }

        var id = baseId;
        var suffix = 2;

        while (Profiles.Any(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            id = $"{baseId}-{suffix}";
            suffix++;
        }

        return id;
    }

    private static FrpProfileStoreDocument ReadStoreDocument(string storePath)
    {
        try
        {
            var json = File.ReadAllText(storePath);
            return JsonSerializer.Deserialize<FrpProfileStoreDocument>(json) ?? new FrpProfileStoreDocument();
        }
        catch
        {
            return new FrpProfileStoreDocument();
        }
    }

    private static string CreateDefaultConfigText()
    {
        return """
               serverAddr = "127.0.0.1"
               serverPort = 7000
               auth.token = "123456"

               """;
    }

    private class FrpProfileStoreDocument
    {
        public string ActiveProfileId { get; set; } = "default";

        public List<FrpProfileInfo> Profiles { get; set; } = [];
    }
}
