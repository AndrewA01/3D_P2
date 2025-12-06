using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveOnWaypoints : MonoBehaviour
{
    [Header("Choose waypoint group (empty object with child cubes)")]
    public Transform waypointParent;   // Drag WaypointsLeft OR WaypointsRight here

    public float speed = 2f;

    private List<Transform> waypoints = new List<Transform>();
    private int index = 0;

    void Start()
    {
        // Load all child transforms from the selected waypoint parent
        if (waypointParent == null)
        {
            Debug.LogError("MoveOnWaypoints: No waypointParent assigned!");
            return;
        }

        waypoints.Clear();

        foreach (Transform child in waypointParent)
        {
            waypoints.Add(child);
        }

        if (waypoints.Count == 0)
            Debug.LogError("MoveOnWaypoints: waypointParent has no children!");
    }

    private void Update()
    {
        if (waypoints.Count == 0) return;

        Vector3 destination = waypoints[index].position;
        transform.position = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);

        if (Vector3.Distance(transform.position, destination) <= 0.05f)
        {
            if (index < waypoints.Count - 1)
                index++;
        }
    }
}
