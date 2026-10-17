using System.Linq;
using NUnit.Framework;
using RoguelikeMap.Model;
using RoguelikeMap.Unity;
using UnityEditor;
using UnityEngine;

namespace SpotlightGameJam.Tests
{
    public sealed class MapNamedAnchorTests
    {
        [Test]
        public void EveryLayoutUses230NodeAndCoincidentNamedEndpoints()
        {
            var asset = AssetDatabase.LoadAssetAtPath<MapGenerationConfigAsset>("Assets/GameData/Map/MapGenerationConfig.asset");
            foreach (var preset in asset.PreGeneratedMaps)
            {
                Assert.That(preset.UseConnectionAnchors, Is.True, preset.name);
                var roots = preset.Nodes.ToDictionary(n => n.NodeId, n => n.PlacementRoot);
                foreach (var binding in preset.Nodes)
                {
                    var root = binding.PlacementRoot;
                    Assert.That(root.rect.size, Is.EqualTo(new Vector2(230, 230)), preset.name);
                    if (binding.View != null) Assert.That(binding.View.transform.parent, Is.SameAs(root));
                }
                foreach (var edge in preset.Edges)
                {
                    var placement = preset.Data.Graph.AssetLayout.FindEdge(edge.FromId, edge.ToId);
                    string a = Name(placement.SourceSide), b = Name(placement.TargetSide);
                    Assert.That(edge.View.transform.Find(a), Is.Not.Null, preset.name + ": " + a);
                    Assert.That(edge.View.transform.Find(b), Is.Not.Null, preset.name + ": " + b);
                    Assert.That(Vector3.Distance(edge.View.transform.Find(a).position, roots[edge.FromId].Find(a).position), Is.LessThan(.02f), preset.name);
                    Assert.That(Vector3.Distance(edge.View.transform.Find(b).position, roots[edge.ToId].Find(b).position), Is.LessThan(.02f), preset.name);
                }
            }
        }

        private static string Name(MapSocketSide side) => side == MapSocketSide.Up ? "top" :
            side == MapSocketSide.Down ? "bottom" : side == MapSocketSide.Left ? "left" : "right";
    }
}
