using UnityEngine;

namespace OshaShelters;

sealed class PositionSetter : UpdatableAndDeletable
{
    public AbstractPhysicalObject o;
    public Vector2[] positions;

    int timer = 10;
    public override void Update(bool eu)
    {
        base.Update(eu);

        if (timer --> 0) {
            o.pos.Tile = room.GetTilePosition(positions[0]);

            if (o.realizedObject == null) {
                return;
            }

            for (int i = 0; i < o.realizedObject.bodyChunks.Length; i++) {
                Vector2 pos = i < positions.Length ? positions[i] : (positions[0] + RWCustom.Custom.RNV());

                o.realizedObject.bodyChunks[i].HardSetPosition(pos);
                o.realizedObject.bodyChunks[i].vel = Vector2.zero;
            }

            if (o.realizedObject is PlayerCarryableItem c) {
                c.lastOutsideTerrainPos = positions[0];
            }

            return;
        }
        Destroy();
    }
}
