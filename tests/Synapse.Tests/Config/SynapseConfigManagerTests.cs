using Shouldly;
using Synapse.Sync.Config;

namespace Synapse.Tests.Config;

public class SynapseConfigManagerTests : IDisposable
{
    private readonly string _tempConfigFile;

    public SynapseConfigManagerTests()
    {
        _tempConfigFile = Path.Combine(Path.GetTempPath(), $"synapse-config-test-{Guid.NewGuid():N}.json");
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ShouldReturnDefaultConfig()
    {
        var manager = new SynapseConfigManager(_tempConfigFile);

        var config = await manager.LoadAsync();

        config.ShouldNotBeNull();
        config.IsConfigured.ShouldBeFalse();
        config.Repository.ShouldBe("Synapse-Vault");
        config.Branch.ShouldBe("main");
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsync_ShouldPersistAndLoadCorrectly()
    {
        var manager = new SynapseConfigManager(_tempConfigFile);
        var original = new SynapseConfig
        {
            VaultPath = "C:\\Users\\User\\Vault",
            Owner = "VictorSilva-Desenvolvedor",
            Repository = "My-Notes",
            Branch = "main",
            DebounceMs = 3000,
            PollingIntervalSeconds = 45,
            ReconciliationIntervalMinutes = 20
        };

        await manager.SaveAsync(original);

        File.Exists(_tempConfigFile).ShouldBeTrue();

        var loaded = await manager.LoadAsync();
        loaded.ShouldNotBeNull();
        loaded.VaultPath.ShouldBe(original.VaultPath);
        loaded.Owner.ShouldBe(original.Owner);
        loaded.Repository.ShouldBe(original.Repository);
        loaded.Branch.ShouldBe(original.Branch);
        loaded.DebounceMs.ShouldBe(3000);
        loaded.PollingIntervalSeconds.ShouldBe(45);
        loaded.ReconciliationIntervalMinutes.ShouldBe(20);
        loaded.IsConfigured.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WhenAllFieldsAreValid_ShouldReturnTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"synapse-vault-valid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new SynapseConfig
            {
                VaultPath = tempDir,
                Owner = "VictorSilva-Desenvolvedor",
                Repository = "Synapse-Vault"
            };

            var isValid = SynapseConfigManager.Validate(config, out var errors);

            isValid.ShouldBeTrue();
            errors.ShouldBeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void Validate_WhenFieldsAreEmpty_ShouldReturnFalseWithErrors()
    {
        var config = new SynapseConfig
        {
            VaultPath = string.Empty,
            Owner = string.Empty,
            Repository = string.Empty
        };

        var isValid = SynapseConfigManager.Validate(config, out var errors);

        isValid.ShouldBeFalse();
        errors.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    public void Dispose()
    {
        if (File.Exists(_tempConfigFile))
        {
            try { File.Delete(_tempConfigFile); } catch { }
        }
    }
}
