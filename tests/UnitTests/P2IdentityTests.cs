using System;
using System.Collections.Generic;
using System.Linq;
using OPBlocks.Core;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// P2 (v1.1.3): every block must be able to introduce itself — full name,
    /// family, and a one-line typical-use hint — from the single BlockCatalog
    /// source. A new block without catalog identity fails here, not in a user's
    /// palette.
    /// </summary>
    public class P2IdentityTests
    {
        static IEnumerable<UnitBase> AllBlocks()
        {
            var assemblies = new[]
            {
                typeof(OPBlocks.Desalination.ReverseOsmosis).Assembly,
                typeof(OPBlocks.Electro.Electrodialysis).Assembly,
                typeof(OPBlocks.Lithium.DirectLithiumExtraction).Assembly,
                typeof(OPBlocks.Energy.PemElectrolyzer).Assembly,
            };
            foreach (var asm in assemblies)
                foreach (var t in asm.GetExportedTypes())
                    if (!t.IsAbstract && typeof(UnitBase).IsAssignableFrom(t))
                        yield return (UnitBase)Activator.CreateInstance(t);
        }

        [Fact]
        public void Every_Block_Has_A_Complete_Catalog_Identity()
        {
            var blocks = AllBlocks().ToList();
            Assert.True(blocks.Count >= 25, "expected the full 25-block library, found " + blocks.Count);
            foreach (UnitBase b in blocks)
            {
                BlockCatalog.Info id = BlockCatalog.For(b.BlockCode);
                Assert.False(string.IsNullOrWhiteSpace(id.FullName), b.BlockCode + ": missing full name");
                Assert.NotEqual(b.BlockCode, id.FullName);
                Assert.False(string.IsNullOrWhiteSpace(id.Family), b.BlockCode + ": missing family");
                Assert.False(string.IsNullOrWhiteSpace(id.TypicalUse), b.BlockCode + ": missing typical-use hint");
            }
        }

        [Fact]
        public void DisplayTitle_Is_Code_First()
        {
            Assert.Equal("OP-RO — Reverse Osmosis", BlockCatalog.DisplayTitle("OP-RO"));
            Assert.Equal("OP-EVAPPOND — Solar Evaporation Pond", BlockCatalog.DisplayTitle("OP-EVAPPOND"));
        }

        [Fact]
        public void Unknown_Code_Degrades_Gracefully()
        {
            Assert.Equal("OP-XYZ", BlockCatalog.DisplayTitle("OP-XYZ"));
            Assert.NotNull(BlockCatalog.For(null));
        }
    }
}
