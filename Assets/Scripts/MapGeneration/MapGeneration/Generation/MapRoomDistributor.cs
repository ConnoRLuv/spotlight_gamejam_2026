using RoguelikeMap.Config;
using RoguelikeMap.Model;
using RoguelikeMap.Rules;
using RoguelikeMap.Serialization;
using RoguelikeMap.Validation;

namespace RoguelikeMap.Generation
{
    /// <summary>只还原已有布局并分配房间；不铺路、不移动节点、不改连接。</summary>
    public static class MapRoomDistributor
    {
        public static MapGenerationResult Assign(MapGraphDto layout, MapGenerationConfig config, NodeTypeCatalog catalog, uint seed)
        {
            var generator = new MapGenerator(MapRuleSet.CreateDefault())
                .AddStep(new RestoreLayout(layout))
                .AddStep(new Steps.AssignNodeTypesStep(true))
                .AddValidator(new LayeredTopologyValidator())
                .AddValidator(new ConnectivityValidator())
                .AddValidator(new RowOccupancyValidator())
                .AddValidator(new PathCountValidator())
                .AddValidator(new FixedRowValidator())
                .AddValidator(new RuleConsistencyValidator());
            var result = generator.Generate(config, catalog, seed);
            if (result.Graph != null)
            {
                result.Graph.Seed = layout.Seed;
                result.Graph.Attempt = layout.Attempt;
            }
            return result;
        }

        private sealed class RestoreLayout : IMapGenerationStep
        {
            private readonly MapGraphDto layout;
            public string Name => "读取空白地图布局";
            public RestoreLayout(MapGraphDto layout) { this.layout = layout; }
            public void Execute(MapGenerationContext context)
            {
                context.Graph = MapSerializer.FromDto(layout);
                foreach (var node in context.Graph.Nodes)
                {
                    bool start = node.Id == context.Graph.StartNodeId;
                    bool boss = context.Config.IncludeBossNode && node.Row == context.Graph.Rows - 1;
                    node.IsExemptFromRules = start || boss;
                    node.TypeKey = start ? context.Config.StartTypeKey : boss ? context.Config.BossTypeKey : NodeTypeKeys.Unassigned;
                }
            }
        }
    }
}
