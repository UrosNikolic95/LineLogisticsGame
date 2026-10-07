
namespace LineLogisticsGame.World
{
    // What a line does in the transport game: roads are only driven on, Output lines produce resources of their Type,
    // Input lines take the Type their station needs to produce, Consumer lines consume every type.
    public enum LineKind { Road, Output, Input, Consumer }
}
