using UnityEngine;

namespace OshaShelters;

sealed class PositionSetter : UpdatableAndDeletable
{
    readonly AbstractPhysicalObject target;
    readonly Vector2[] chunks;

    public PositionSetter(AbstractPhysicalObject target, Vector2[] chunks)
    {
        this.target = target;
        this.chunks = chunks;
        for (int i = 0; i < this.chunks.Length; i++) {
            this.chunks[i] += RWCustom.Custom.RNV();
        }
    }

    int timer = 10;
    public override void Update(bool eu)
    {
        base.Update(eu);

        if (timer --> 0) {
            target.pos.Tile = room.GetTilePosition(chunks[0]);

            if (target.realizedObject == null) {
                return;
            }

            for (int i = 0; i < target.realizedObject.bodyChunks.Length; i++) {
                Vector2 pos = i < chunks.Length ? chunks[i] : chunks[0] + RWCustom.Custom.RNV();

                target.realizedObject.bodyChunks[i].HardSetPosition(pos);
                target.realizedObject.bodyChunks[i].vel = Vector2.zero;
            }

            return;
        }
        Destroy();
    }
}
