using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Dijkstra.NET.Graph;
using Dijkstra.NET.ShortestPath;
using System.Linq;

public class PathfinderAI : MonoBehaviour
{
    /// <summary>
    /// Contains the tilemap of the cells of the map
    /// </summary>
    [SerializeField] private Tilemap groundTilemap;
    
    /// <summary>
    /// Contains the tilemap of the cells of all the platforms
    /// </summary>
    [SerializeField] private Tilemap platformTilemap;

    /// <summary>
    /// Contains all the grid locations in the tilemap that have obstacles
    /// </summary>
    private List<Vector3Int> grid;

    /// <summary>
    /// Stores way-points as Vector3Int, the points refer to the cell locations above the ground
    /// </summary>
    private Graph<Vector3Int, string> graph;

    public GameObject markerPrefab;

    /// <summary>
    /// Maximum distance allowed to drop
    /// </summary>
    [SerializeField] private float maxDropDistance;

    /// <summary>
    /// Defines which layers are considered obstacles
    /// </summary>
    [SerializeField] private LayerMask obstaclesMask;

    /// <summary>
    /// Measured as the number of blocks between the TOP edge of standing block and the TOP edge of landing block
    /// </summary>
    [SerializeField] private int jumpHeight;

    /// <summary>
    /// Measured as the number of blocks between the LEFT edge of standing block and the RIGHT edge of landing block
    /// </summary>
    [SerializeField] private int jumpDistance;

    private float cellSizeX;
    private float cellSizeY;

    /// <summary>
    /// Used for debugging, draws lines of way-points and possible paths
    /// </summary>
    [SerializeField] private bool drawLines;

    private void Start() {
        // Might run into problem of duplicates in graph! ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
        graph = new Graph<Vector3Int, string>();
        cellSizeX = groundTilemap.cellSize.x;
        cellSizeY = groundTilemap.cellSize.y;

        // This creates a Grid Graph
        createMap();

        // Connect all the points in the graph
        connectPoints();
    }

    private void Update() {
        if (Input.GetKeyDown(KeyCode.P)) { 
            debugGraph();
        }

        // Testing purposes
        if (Input.GetKeyDown(KeyCode.Mouse1)) { 
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3Int cellPosition = groundTilemap.WorldToCell(mousePosition);
            print("Mouse press: " + mousePosition);
            print("Cell position: " + cellPosition);
        }

        // Testing purposes
        if (Input.GetKeyDown(KeyCode.Mouse2)) { 
            Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            var hit = Physics2D.Raycast(mousePosition, Vector2.down, 1000f, obstaclesMask);
            if (hit) {
                // Shift point down
                hit.point = hit.point - Vector2.up * 0.3f;
                Vector3Int cellPosition = groundTilemap.WorldToCell(hit.point);
                print("Ray hit position: " + hit.point);
                print("Ray cell position: " + cellPosition);
                Debug.DrawLine(mousePosition, hit.point, Color.green, 30f);
            }
        }
    }

    private void createMap() {
        // Create all used grid locations
        generateUsedGridLocations();

        foreach (var location in grid) {
            var locationType = getLocationType(location);

            // If location is valid, ie not null, and there is a drop or wall on either side
            if (locationType.x != -100 && locationType != Vector2Int.zero) {
                addAboveLocationToGraph(location);

                // check if left side is a drop
                if (locationType.x == -1) {
                    // Create starting position + add a little buffer to center the raycast
                    Vector3 startPosition = new Vector3Int(location.x - 1, location.y,  location.z) + Vector3.one * (cellSizeX / 2);

                    // Racasts downward
                    RaycastHit2D hit = Physics2D.Raycast(startPosition, Vector3.down, maxDropDistance, obstaclesMask);
                    if (hit) {
                        // Extend the hit point down in order to make sure point is inside correct grid point
                        var newPoint = hit.point - Vector2.up * 0.3f;
                        addAboveLocationToGraph(groundTilemap.WorldToCell(newPoint));

                        // Draws the drop lines
                        if (drawLines)
                            Debug.DrawLine(startPosition, newPoint, Color.cyan, 1000f);
                    }
                }

                // check if right side is a drop
                if (locationType.y == -1) {
                    // Create starting position + add a little buffer to center the raycast
                    Vector3 startPosition = new Vector3Int(location.x + 1, location.y,  location.z) + Vector3.one * (cellSizeX / 2);
                    
                    // Racasts downward
                    RaycastHit2D hit = Physics2D.Raycast(startPosition, Vector3.down, maxDropDistance, obstaclesMask);
                    if (hit) {
                        // Extend the hit point down in order to make sure point is inside correct grid point
                        var newPoint = hit.point - Vector2.up * 0.3f;
                        addAboveLocationToGraph(groundTilemap.WorldToCell(newPoint));

                        // Draws the drop lines
                        if (drawLines)
                            Debug.DrawLine(startPosition, newPoint, Color.cyan, 1000f);
                    }
                }
            }

            // Checks for platform drops now
            if (platformTilemap.HasTile(location)) { 
                // Create starting position + add a little buffer to center the raycast and start below the tile
                    Vector3 startPosition = new Vector3Int(location.x, location.y,  location.z) + Vector3.one * (cellSizeX / 2);

                    // Racasts downward
                    RaycastHit2D hit = Physics2D.Raycast(startPosition, Vector3.down, maxDropDistance, obstaclesMask);
                    if (hit) {
                        // Extend the hit point down in order to make sure point is inside correct grid point
                        var newPoint = hit.point - Vector2.up * 0.3f;
                        addAboveLocationToGraph(groundTilemap.WorldToCell(newPoint));

                        addAboveLocationToGraph(location);

                        // Draws the drop lines
                        if (drawLines)
                            Debug.DrawLine(startPosition, newPoint, Color.cyan, 1000f);
                    }
            }
        }
        
    }

    private void connectPoints() {
        // Loop through all the points
        foreach (var point in graph) {
            // Stores the "closest right"
            // We start closeRight to Vector3Int.back so it has a negative z-component
            (Vector3Int, uint) closeRight = (Vector3Int.back, 0);
            (Vector3Int, uint) closeLeftDrop = (Vector3Int.back, 0);
            (Vector3Int, uint) closeRightDrop = (Vector3Int.back, 0);

            (Vector3Int, uint) closePlatformDrop = (Vector3Int.back, 0);

            var location = point.Item;

            // We subtract 1 because we want the location type of the tile blow it
            var locationType = getLocationType(location - Vector3Int.up);

            var pointsToJoin = new List<(Vector3Int, uint)>();
            var noBiJoin = new List<(Vector3Int, uint)>();

            foreach (var newPoint in graph) {
                var newLocation = newPoint.Item;

                // If the second point is at the same height as the first point and is to the right of it
                if (locationType.y == 0 && newLocation.y == location.y && newLocation.x > location.x) {
                    // If closeRight isnt set or the new location is closer, then set new closest right
                    if (closeRight.Item1.z < 0 || newLocation.x < closeRight.Item1.x) {
                        closeRight.Item1 = newPoint.Item;
                        closeRight.Item2 = newPoint.Key;
                    }
                }

                // Check if left side is a drop
                if (locationType.x == -1) {
                    // if the new location is one unit to the left of location and the new location is higher than the current location
                    if (newLocation.x == location.x - (int)cellSizeX && newLocation.y < location.y) {
                        if (closeLeftDrop.Item1.z < 0 || newLocation.y > closeLeftDrop.Item1.y) {
                            closeLeftDrop.Item1 = newPoint.Item;
                            closeLeftDrop.Item2 = newPoint.Key;
                        }
                    }

                    // Check distance jumps on the left
                    if ((newLocation.y <= location.y + (cellSizeY * jumpHeight)) && (newLocation.y >= location.y)
                        && (newLocation.x > location.x - (cellSizeX * (jumpDistance + 2))) && (newLocation.x < location.x)
                        && getLocationType(newLocation - Vector3Int.up).y == -1 && !grid.Contains(location + Vector3Int.up * (int)cellSizeY)) {
                        
                        pointsToJoin.Add((newPoint.Item, newPoint.Key));
                    }
                }

                // Check if right side is a drop
                if (locationType.y == -1) {
                    // if the new location is one unit to the right of location and the new location is higher than the current location
                    if (newLocation.x == location.x + (int)cellSizeX && newLocation.y < location.y) {
                        if (closeRightDrop.Item1.z < 0 || newLocation.y > closeRightDrop.Item1.y) {
                            closeRightDrop.Item1 = newPoint.Item;
                            closeRightDrop.Item2 = newPoint.Key;
                        }
                    }

                    // Check distance jumps on the right
                    if ((newLocation.y <= location.y + (cellSizeY * jumpHeight)) && (newLocation.y >= location.y)
                        && (newLocation.x < location.x + (cellSizeX * (jumpDistance + 2))) && (newLocation.x > location.x)
                        && getLocationType(newLocation - Vector3Int.up).x == -1 && !grid.Contains(location + Vector3Int.up * (int)cellSizeY)) {
                        
                        pointsToJoin.Add((newPoint.Item, newPoint.Key));
                    }
                }

                // Platform points handling
                if (platformTilemap.HasTile(location - Vector3Int.up) && newLocation.y < location.y && newLocation.x == location.x) {
                    if (closePlatformDrop.Item1.z < 0 || newLocation.y > closePlatformDrop.Item1.y) {
                        closePlatformDrop.Item1 = newPoint.Item;
                        closePlatformDrop.Item2 = newPoint.Key;
                    }
                }
            }

            // If a closer right is chosen then draw it, connect it and add it to the graph
            if (closeRight.Item1.z >= 0) {
                pointsToJoin.Add(closeRight);
            }

            // If a left drop is chosen then draw it, connect it and add it to the graph
            if (closeLeftDrop.Item1.z >= 0) {

                // If the pathfinder can jump to the location above it then make the connection a bi-route
                if (closeLeftDrop.Item1.y >= location.y - cellSizeY * jumpHeight)
                    pointsToJoin.Add(closeLeftDrop);
                else
                    noBiJoin.Add(closeLeftDrop);

            }

            // If a right drop is chosen then draw it, connect it and add it to the graph
            if (closeRightDrop.Item1.z >= 0) {

                // If the pathfinder can jump to the location above it then make the connection a bi-route
                if (closeRightDrop.Item1.y >= location.y - cellSizeY * jumpHeight)
                    pointsToJoin.Add(closeRightDrop);
                else
                    noBiJoin.Add(closeRightDrop);
            }

            // Add platform drops
            if (closePlatformDrop.Item1.z >= 0) {
                // If the pathfinder can jump to the location above it then make the connection a bi-route
                if (closePlatformDrop.Item1.y >= location.y - cellSizeY * jumpHeight)
                    pointsToJoin.Add(closePlatformDrop);
                else
                    noBiJoin.Add(closePlatformDrop);
            }
    
            // Add all the points to join into the graph, twice b/c bijection
            foreach (var join in pointsToJoin) {
                graph.Connect(point.Key, join.Item2, (int)Vector3Int.Distance(point.Item, join.Item1), "");
                graph.Connect(join.Item2, point.Key, (int)Vector3Int.Distance(point.Item, join.Item1), "");

                if (drawLines)
                    drawLine(point.Item, join.Item1);
            }

            // Add all the points to join into the graph, but only once
            foreach (var join in noBiJoin) {
                graph.Connect(point.Key, join.Item2, (int)Vector3Int.Distance(point.Item, join.Item1), "");

                if (drawLines)
                    drawLine(point.Item, join.Item1);
            }
        }
    }

    public Queue<Vector3> findPath(Vector3 start, Vector3 end) {
        // Calibrate the end point so it is not inside the floor
        end = groundTilemap.WorldToCell(end) + Vector3Int.up;

        var startPointID = getClosestPointID(groundTilemap.WorldToCell(start));
        var endPointID = getClosestPointID(groundTilemap.WorldToCell(end));
        
        if (startPointID == endPointID) {
            var queue =new Queue<Vector3>();
            queue.Enqueue(end);
            return queue;
        }

        // Create a path by using Dijkstra algo.
        var path = graph.Dijkstra(startPointID, endPointID).GetPath();

        Queue<Vector3> actions = new Queue<Vector3>();
        Vector3Int previousLocation = Vector3Int.back;

        var pathArray = Enumerable.ToArray<uint>(path);
        
        for (int i = 0; i < pathArray.Length; i++) {
            var location = graph[pathArray[i]].Item;
            var locationType = getLocationType(location - Vector3Int.up);

            // Checks if jump is needed
            // Checks if previous location exists, the previous location was below this one, but can be reached with a jump
            // and that the location is at a different x position and that this location is a 'ledge'
            if (previousLocation.z >= 0 && previousLocation.y <= location.y + (cellSizeY * jumpHeight)
                && ((previousLocation.x < location.x && locationType.x == -1) || previousLocation.x > location.x 
                && locationType.y == -1)) {

                // Logic to decide proper jump height
                int miniumJumpHeight = jumpHeight;
                for (int j = jumpHeight; j > 0; j--) {
                    if (previousLocation.y + (cellSizeY * j) >= location.y && j < jumpHeight) {
                        miniumJumpHeight = j;
                    }
                }

                // The jump vector will be indicated with a z-component > 0, where x and y are the deltaX and deltaY that the pathfinder needs to travel across
                actions.Enqueue(new Vector3(location.x - previousLocation.x - (cellSizeX / 2), location.y - previousLocation.y - (cellSizeY / 2), miniumJumpHeight));
            }
            else if (previousLocation.z >= 0 && platformTilemap.HasTile(location - Vector3Int.up) && previousLocation.y < location.y && previousLocation.y + (cellSizeY * jumpHeight) >= location.y && location.x == previousLocation.x) {
                
                // Logic to decide proper jump height
                int miniumJumpHeight = jumpHeight;
                for (int j = jumpHeight; j > 0; j--) {
                    if (previousLocation.y + (cellSizeY * j) >= location.y && j < jumpHeight) {
                        miniumJumpHeight = j;
                    }
                }

                // The jump vector will be indicated with a z-component > 0, where x and y are the deltaX and deltaY that the pathfinder needs to travel across
                actions.Enqueue(new Vector3(location.x - previousLocation.x - (cellSizeX / 2), location.y - previousLocation.y - (cellSizeY / 2), miniumJumpHeight));
            }

            // Checks for drop-down
            if (previousLocation.z >= 0 && platformTilemap.HasTile(previousLocation - Vector3Int.up) && location.y < previousLocation.y && location.x == previousLocation.x) {
                actions.Enqueue(new Vector3(location.x, location.y, -2));
            }
            previousLocation = location;
            
            if (i == 0 && pathArray.Length > 1) {
                var nextLocation = graph[pathArray[1]].Item;
                if (Vector3.Distance(start, nextLocation) > Vector3.Distance(location, nextLocation)) {
                    actions.Enqueue(location);
                }
            }
            else if (i == pathArray.Length - 1 && pathArray.Length > 1) {
                // pointID is pointing to last node
                var secondToLastLocation = graph[pathArray[pathArray.Length - 2]].Item;
                if (Vector3.Distance(end, secondToLastLocation) < Vector3.Distance(location, end) || location.y != secondToLastLocation.y) {
                    actions.Enqueue(location);
                }
            }
            else {
                actions.Enqueue(location);
            }
        }
        
        if (actions.Count > 0 || groundTilemap.WorldToCell(start).y == groundTilemap.WorldToCell(end).y)
            actions.Enqueue(end);
        
        return actions;
    }

    public Vector2 getOptimalIntialVelocity(float deltaX, float deltaY, float velocityX, float accelerationY) {
        // Using kinematics
        float totalTime =  Mathf.Abs(deltaX / velocityX);

        float intialVelocityY = deltaY / totalTime - 0.5f * accelerationY * totalTime;

        return new Vector2(0, intialVelocityY * 1.1f);
    }

    public Vector3 getCellCenter(Vector3 vec) {
        return vec + new Vector3(cellSizeX / 2, cellSizeY / 2, 0);
    }

    private void drawLine(Vector3Int start, Vector3Int end) {
        Debug.DrawLine(groundTilemap.CellToWorld(start) + Vector3.one * (groundTilemap.cellSize.x / 2), groundTilemap.CellToWorld(end) + Vector3.one * (groundTilemap.cellSize.x / 2), Color.red, 1000f);
    }

    private void addAboveLocationToGraph(Vector3Int location) {
        // The cell above each used location
        var aboveCell = new Vector3Int(location.x, location.y + 1, location.z);

        // If the grid does not contain the point already...
        if (!grid.Contains(aboveCell)) {
            // If the graph already has elements and contains this point, then skip adding it
            if (graph.NodesCount > 0 && graphContains(aboveCell)) {
                return;
            }

            // Creates a gameobject as an indicator
            Vector3 position = groundTilemap.CellToWorld(aboveCell) + new Vector3(groundTilemap.cellSize.x / 2, groundTilemap.cellSize.y / 2, 0);
            //Instantiate(markerPrefab, position, Quaternion.identity);

            // Add point to graph
            graph.AddNode(aboveCell);
        }
    }

    private uint getClosestPointID(Vector3Int location) {
        float shortestDistance = float.MaxValue;
        uint closestID = 0;

        // Loop through all the points in the graph
        foreach (var point in graph)
        {
            if (point.Item.y == location.y) {
                if (point.Item == location) {
                    return point.Key;
                }
            
                // Compare distance to stored shortest distance
                if (Mathf.Abs(location.x - point.Item.x) < shortestDistance) {
                    closestID = point.Key;
                    shortestDistance = Mathf.Abs(location.x - point.Item.x);
                }
            }            
        }
        if (shortestDistance == float.MaxValue) {
            throw new System.Exception("Cannot find point for: " + location);
        }

        return closestID;
    }

    private bool graphContains(Vector3Int location) {
        foreach (var position in graph) {
            if (position.Item.Equals(location)) {
                return true;
            }
        }
        return false;
    }

    private Vector2Int getLocationType(Vector3Int location) {
        // Checks if there exists a tile above the given location, if so return "invalid" input
        if (grid.Contains(new Vector3Int(location.x, location.y + 1, location.z))) {
            return new Vector2Int(-100, -100);
        }

        // This stores the information of the left and right side of the given location
        // x repersents left side
        // y repersents right side
        // value can be -1, if there is a drop, 1 if there is a wall, and 0 if it is open
        Vector2Int result = new Vector2Int(0, 0);

        // If the position to left and up is in grid, then there is a wall
        // Else if only the left position is not in the grid, then it's a "drop"
        if (grid.Contains(new Vector3Int(location.x - 1, location.y + 1, location.z))) {
            result.x = 1;
        }
        else if (!grid.Contains(new Vector3Int(location.x - 1, location.y, location.z))) {
            result.x = -1;
        }

        // Do same thing except check the right side of location
        if (grid.Contains(new Vector3Int(location.x + 1, location.y + 1, location.z))) {
            result.y = 1;
        }
        else if (!grid.Contains(new Vector3Int(location.x + 1, location.y, location.z))) {
            result.y = -1;
        }

        return result;
    }

    private void generateUsedGridLocations() {
        // Creates used grid locations by looping through all the tilemap cells
        grid = new List<Vector3Int>();
        foreach (var pos in groundTilemap.cellBounds.allPositionsWithin)
        {   
            Vector3Int localPlace = new Vector3Int(pos.x, pos.y, pos.z);
            // Checks if there a tile exists in that location
            if (groundTilemap.HasTile(localPlace))
            {
                grid.Add(localPlace);
            }
        }

        // Add all platform locations, platform tiles and ground tiles should not overlap
        foreach(var pos in platformTilemap.cellBounds.allPositionsWithin)
        {
            Vector3Int localPlace = new Vector3Int(pos.x, pos.y, pos.z);
            // Checks if there a tile exists in that location
            if (platformTilemap.HasTile(localPlace))
            {
                if (groundTilemap.HasTile(localPlace))
                {
                    throw new System.Exception("A PLATFORM AND GROUND TILE ARE OVERLAPPING AT: " + localPlace);
                }
                grid.Add(localPlace);
            }
        }
    }

    private void debugGraph() {
        if (graph.NodesCount > 1) {
            foreach (var point in graph) {
                Debug.DrawRay(point.Item, Vector3.up * 0.5f, Color.blue, 100f);
                
            }
        }
    }
}