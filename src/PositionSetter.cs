using UnityEngine;

namespace OshaShelters;

sealed class PositionSetter : UpdatableAndDeletable
{
    public AbstractPhysicalObject o;
    public Vector2 pos;

    int timer = 10;
    public override void Update(bool eu)
    {
        base.Update(eu);

        if (timer-- > 0) {
            o.pos.Tile = room.GetTilePosition(pos);
            o.realizedObject?.firstChunk.HardSetPosition(pos);

            if (o.realizedObject is PlayerCarryableItem c) c.lastOutsideTerrainPos = pos;
        }
        else {
            Destroy();
        }
    }
}
