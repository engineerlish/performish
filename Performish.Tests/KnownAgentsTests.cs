using System.Collections.Generic;
using Performish.Core.Compatibility;
using Xunit;

namespace Performish.Tests
{
    public class KnownAgentsTests
    {
        [Fact]
        public void Detect_MatchesKnownProcessName_CaseInsensitive()
        {
            var found = KnownAgents.Detect(new[] { "explorer", "TEAMVIEWER", "chrome" });
            Assert.Contains(found, a => a.ProcessName == "teamviewer");
        }

        [Fact]
        public void Detect_NoMatches_ReturnsEmpty()
        {
            var found = KnownAgents.Detect(new[] { "explorer", "chrome", "notepad" });
            Assert.Empty(found);
        }

        [Fact]
        public void Detect_EmptyList_ReturnsEmpty_DoesNotThrow()
        {
            var found = KnownAgents.Detect(new List<string>());
            Assert.Empty(found);
        }

        [Fact]
        public void Detect_NullList_ReturnsEmpty_DoesNotThrow()
        {
            var found = KnownAgents.Detect(null);
            Assert.Empty(found);
        }

        [Fact]
        public void All_HasNoDuplicateProcessNames()
        {
            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var a in KnownAgents.All)
                Assert.True(names.Add(a.ProcessName), $"Duplicate process name: {a.ProcessName}");
        }

        [Fact]
        public void All_EveryEntryHasDisplayNameAndCategory()
        {
            foreach (var a in KnownAgents.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(a.ProcessName));
                Assert.False(string.IsNullOrWhiteSpace(a.DisplayName));
                Assert.False(string.IsNullOrWhiteSpace(a.Category));
            }
        }
    }
}
