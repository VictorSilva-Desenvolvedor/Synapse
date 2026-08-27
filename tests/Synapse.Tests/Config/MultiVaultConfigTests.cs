using Shouldly;
using Synapse.Sync.Config;

namespace Synapse.Tests.Config;

public class MultiVaultConfigTests
{
    [Fact]
    public void GetActiveVaults_WhenLegacySingleVaultConfigured_ShouldReturnDefaultProfile()
    {
        var config = new SynapseConfig
        {
            VaultPath = "C:\\Vault",
            Owner = "VictorSilva-Desenvolvedor",
            Repository = "MeuRepo",
            Branch = "main"
        };

        config.IsConfigured.ShouldBeTrue();

        var activeVaults = config.GetActiveVaults();
        activeVaults.Count.ShouldBe(1);
        activeVaults[0].Name.ShouldBe("Principal");
        activeVaults[0].VaultPath.ShouldBe("C:\\Vault");
        activeVaults[0].Repository.ShouldBe("MeuRepo");
    }

    [Fact]
    public void GetActiveVaults_WhenMultipleVaultsConfigured_ShouldReturnOnlyEnabledAndConfigured()
    {
        var config = new SynapseConfig
        {
            Vaults =
            [
                new VaultProfile
                {
                    Name = "Pessoal",
                    VaultPath = "C:\\VaultPessoal",
                    Owner = "VictorSilva-Desenvolvedor",
                    Repository = "Vault-Pessoal",
                    IsEnabled = true
                },
                new VaultProfile
                {
                    Name = "Trabalho",
                    VaultPath = "C:\\VaultTrabalho",
                    Owner = "VictorSilva-Desenvolvedor",
                    Repository = "Vault-Trabalho",
                    IsEnabled = true
                },
                new VaultProfile
                {
                    Name = "Desativado",
                    VaultPath = "C:\\VaultInativo",
                    Owner = "VictorSilva-Desenvolvedor",
                    Repository = "Vault-Inativo",
                    IsEnabled = false
                }
            ]
        };

        config.IsConfigured.ShouldBeTrue();

        var activeVaults = config.GetActiveVaults();
        activeVaults.Count.ShouldBe(2);
        activeVaults.Any(v => v.Name == "Pessoal").ShouldBeTrue();
        activeVaults.Any(v => v.Name == "Trabalho").ShouldBeTrue();
        activeVaults.Any(v => v.Name == "Desativado").ShouldBeFalse();
    }
}
