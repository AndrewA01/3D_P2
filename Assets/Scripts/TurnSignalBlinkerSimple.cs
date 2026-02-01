using UnityEngine;

// Minimal legacy shim so older scripts (like MoveOnWaypoints) compile.
// You do NOT need to assign this anywhere. MoveOnWaypoints will prefer V2 automatically.
public class TurnSignalBlinkerSimple : MonoBehaviour
{
    public virtual void OnMergeStarted() { }
    public virtual void OnMergeEnded() { }
}
