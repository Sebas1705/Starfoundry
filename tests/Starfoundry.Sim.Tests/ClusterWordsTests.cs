using Starfoundry.Ui;
using Xunit;

namespace Starfoundry.Sim.Tests;

/// <summary>ADR 0046: the words the client uses for clusters exist in both languages and take their arguments.</summary>
[Collection("LocalisedText")]
public class ClusterWordsTests
{
    [Fact]
    public void Cluster_words_and_the_scan_toast_exist_in_both_languages()
    {
        try
        {
            foreach (var lang in new[] { Language.Es, Language.En })
            {
                Loc.Lang = lang;
                foreach (var key in new[] { "cluster.sphere", "cluster.belt", "cluster.shell" })
                {
                    string text = Loc.T(key, "ore");
                    Assert.Contains("ore", text);
                    Assert.NotEqual(key, text);
                }
                string tip = Loc.T("tip.cluster", "x", 3);
                Assert.Contains("3", tip);
                Assert.Contains("x", tip);
                string toast = Loc.T("toast.scanCluster", 7, "y");
                Assert.Contains("7", toast);
                Assert.Contains("y", toast);
            }
            Loc.Lang = Language.Es;
            Assert.StartsWith("Cúmulo", Loc.T("cluster.sphere", "hielo"));
            Loc.Lang = Language.En;
            Assert.EndsWith("belt", Loc.T("cluster.belt", "Ice"));
        }
        finally { Loc.Lang = Language.En; }
    }
}
