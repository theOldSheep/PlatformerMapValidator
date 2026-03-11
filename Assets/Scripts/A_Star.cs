using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class A_Star : MonoBehaviour
{
    [SerializeField] private StateManager stateManager;
    private IMovement playerMovement;
    private PlayerMovement playerComponent; // For action access

    //public variables
    [Header("Start and Goal Positions")]
    [SerializeField] GameObject startPos;
    [SerializeField] GameObject goalPos;


    private Vector2 startPosition;
    private Vector2 goalPosition;

    [Header("Obstacle Check (Raycast)")]
    public LayerMask obstacleLayer;

    [Header("Object to Move")]
    public GameObject playerObject;

    ///TODO: also get movement functions

    //private variables
    A_StarNode currentNode; //record node that A* is currently at
    int updateNumber = 0; //number of updates we've done (use for g)?? ///

    private float moveSpeed = 1f;
    private float jumpForce = 1f;
    private float gravityForce = 0.5f;
    private float halfWidth = 0.5f;
    private float halfHeight = 0.5f; ///TODO: get all these from player object??

    //raycasting variables
    private float checkDistanceJump; ///TODO: should we make these public and let user set these??
    private float checkDistanceFall;
    private float checkDistanceX;

    private Vector2 rayOffset = new Vector2(0f, 0f);

    //arraylist for unexplored nodes and nodes added to the path
    private ArrayList nodeList = new ArrayList(); ///TODO: more efficient data structure?
    private ArrayList closedList = new ArrayList(); ///TODO: show final (fastest) path or total path taken by A*?


    // Start is called before the first frame update
    void Start()
    {
        playerMovement = playerObject.GetComponent<IMovement>();
        playerComponent = playerObject.GetComponent<PlayerMovement>();
        
        startPosition = startPos.transform.position;
        goalPosition = goalPos.transform.position;

        // Initialize search with current state
        GameStateSnapshot startState = stateManager.CaptureState();
        currentNode = ScriptableObject.CreateInstance<A_StarNode>();
        currentNode.nodeSetup(startState, startPosition, 0, Vector2.Distance(startPosition, goalPosition));
        nodeList.Add(currentNode);
    }

    //helper function to print arraylists
    void printNodeList()
    {
        string list = "";
        for (int i = 0; i < nodeList.Count; ++i)
        {
            var nextPos = ((A_StarNode)nodeList[i]).getPosition();
            var nextF = ((A_StarNode)nodeList[i]).getF();
            list += ("[" + nextPos + ", " + nextF + "], ");
        }
        Debug.Log("CURRENT NODE LIST: " + list);
    }

    void printClosedList()
    {
        string list = "";
        for (int i = 0; i < closedList.Count; ++i)
        {
            var nextPos = ((A_StarNode)closedList[i]).getPosition();
            var nextF = ((A_StarNode)closedList[i]).getF();
            list += ("[" + nextPos + ", " + nextF + "], ");
        }
        Debug.Log("CURRENT CLOSED LIST: " + list);
    }

    /*///
    void printList(ArrayList printList){
        string list = "";
        for (int i = 0; i < printList.Count; ++i){
            var nextPos = ((A_StarNode)printList[i]).getPosition();
            var nextF = ((A_StarNode)printList[i]).getF();
            list += ("[" + nextPos + ", " + nextF + "], ");
        }
        Debug.Log(list);
    }
    *////

    // Update is called once per frame
    
    void Update()
    {
        if (Vector2.Distance(playerObject.transform.position, goalPosition) < 1f) return;

        // 1. Reset world to the state we want to explore from
        stateManager.RestoreState(currentNode.State);

        // 2. Ask the character for available actions (Generalization)
        var actions = playerComponent.GetCandidateActions();

        foreach (var action in actions)
        {
            // Revert state for every "branch" attempt
            stateManager.RestoreState(currentNode.State);

            // 3. Apply the generic action
            playerComponent.ApplyAction(action);
            
            // 4. Simulate physics forward by one step (dt)
            playerComponent.SimulateStep(Time.fixedDeltaTime);

            // 5. Record the result
            GameStateSnapshot nextState = stateManager.CaptureState();
            Vector2 nextPos = playerObject.transform.position;

            A_StarNode newNode = createNewNode(nextState, nextPos, currentNode);
            if (nodeNotClosed(newNode))
            {
                addNodeAndGetSmallest(newNode);
            }
        }

        // 6. Move to the best state found
        currentNode = GetSmallestFromOpenList();
        stateManager.RestoreState(currentNode.State);
        closeNode(currentNode);
    }


    // Helper to wrap node creation with the new Snapshot
    A_StarNode createNewNode(GameStateSnapshot state, Vector2 pos, A_StarNode parent)
    {
        float newG = parent.getG() + Vector2.Distance(pos, parent.getPosition());
        float newH = Vector2.Distance(pos, goalPosition);
        
        A_StarNode newNode = ScriptableObject.CreateInstance<A_StarNode>();
        newNode.nodeSetup(state, pos, newG, newH);
        return newNode;
    }

    private bool IsValidMove(Vector2 direction)
    {
        Vector2 origin = (Vector2)transform.position + rayOffset;

        if (direction == Vector2.left)
        {
            return !Physics2D.Raycast(origin, Vector2.left, checkDistanceX, obstacleLayer).collider;
        }
        if (direction == Vector2.right)
        {
            return !Physics2D.Raycast(origin, Vector2.right, checkDistanceX, obstacleLayer).collider;
        }
        if (direction == Vector2.up) // Jump Check
        {
            // Must have no obstacle above and be standing on something
            bool hitUp = Physics2D.Raycast(origin, Vector2.up, checkDistanceJump, obstacleLayer).collider;
            bool hitDown = Physics2D.Raycast(origin, Vector2.down, checkDistanceFall, obstacleLayer).collider;
            return !hitUp && hitDown;
        }
        if (direction == Vector2.down) // Fall Check
        {
            return !Physics2D.Raycast(origin, Vector2.down, checkDistanceFall, obstacleLayer).collider;
        }

        return false;
    }

    private void ApplyMovement(Vector2 direction)
    {
        if (direction == Vector2.up)
        {
            transform.position += Vector3.up * jumpForce;
        }
        else if (direction == Vector2.down)
        {
            transform.position += Vector3.down * gravityForce;
        }
        else
        {
            // Handles Left and Right
            transform.position += (Vector3)direction * moveSpeed;
        }
    }

    private A_StarNode GetSmallestFromOpenList()
    {
        if (nodeList.Count == 0) return null;

        A_StarNode smallestNode = (A_StarNode)nodeList[0];

        for (int i = 1; i < nodeList.Count; i++)
        {
            A_StarNode openNode = (A_StarNode)nodeList[i];
            
            // Primary: Lowest F. Secondary: Highest G (tie-breaker)
            if (openNode.getF() < smallestNode.getF() || 
            (openNode.getF() == smallestNode.getF() && openNode.getG() > smallestNode.getG()))
            {
                smallestNode = openNode;
            }
        }

        return smallestNode;
    }

    ///TODO: find another way to calculate h(n) and/or g(n) that has fewer ties??
    ///TODO: how much should we round f(n) and g(n)???


    //helper function to check if node was already eliminated
    bool nodeNotClosed(A_StarNode newNode)
    {

        Debug.Log("CHECK NODE: pos = " + newNode.getPosition() + ", F = " + newNode.getF() + ", G = " + newNode.getG() + ", H = " + newNode.getH());
        printClosedList();

        //if node is already in closed list, don't add to open nodes list
        foreach (A_StarNode closedNode in closedList)
        {

            /*
            Debug.Log("checking closed node with pos = " + closedNode.getPosition());
            Debug.Log("new node has pos = " + newNode.getPosition());
            if(closedNode.getPosition() == newNode.getPosition()){
                Debug.Log("position already in list");
            }
            */

            if (newNode.isEqual(closedNode))
            {
                Debug.Log("node is already in closed list");
                return false;
            }
        }
        //otherwise node hasn't been closed already
        return true;
    }


    //helper function to close a node
    void closeNode(A_StarNode removeNode)
    {

        Debug.Log("REMOVE A NODE: position = " + removeNode.getPosition() + ", f = " + removeNode.getF());
        ///printNodeList();

        int len = nodeList.Count;
        ///Debug.Log("length of nodeList = " + len);

        //find the node to remove in the open list
        for (int i = 0; i < len; ++i)
        {
            A_StarNode openNode = (A_StarNode)nodeList[i];

            //remove it from the open nodeList and end
            if (removeNode.isEqual(openNode))
            {
                ///Debug.Log("found node to remove at index " + i);
                nodeList.RemoveAt(i);
                break;
            }
        }

        //add it to the closed list
        closedList.Add(removeNode);
        printNodeList();
        ///TODO: figure out if we want to show the whole path including backtracking (then 
        /// we could just show closedList) or only the fastest version (then we'd have to 
        /// store node parents)
    }


    //helper function to find the next node with smallest F
    A_StarNode addNodeAndGetSmallest(A_StarNode newNode)
    {

        ///Debug.Log("ADD NODE");
        ///printNodeList();
        float newNodeF = newNode.getF();
        float newNodeG = newNode.getG();
        Vector2 newNodePos = newNode.getPosition();
        ///Debug.Log("adding a node: pos = " + newNodePos + ", F = " + newNodeF + ", G = " + newNodeG + ", H = " + newNode.getH());


        //place new node and return the node with the current smallest F
        int len = nodeList.Count;
        //if there are zero nodes in the list, add the new one and return it
        if (len == 0)
        {
            nodeList.Add(newNode);
            ///Debug.Log("default added node: pos = " + newNodePos + ", F = " + newNodeF);
            return newNode;
        }

        //otherwise start with newNode as smallest
        A_StarNode smallestNode = newNode;
        //track whether we replaced a node
        bool shouldAddNode = true;

        //scan through list from beginning and check each node
        /*
        float newNodeF = newNode.getF();
        float newNodeG = newNode.getG();
        Vector2 newNodePos = newNode.getPosition();
        */

        //start at beginning of list and go all the way through to the end
        for (int i = 0; i < len; ++i)
        {
            //compare F values to find the new smallest F and/or replace
            A_StarNode openNode = (A_StarNode)nodeList[i];
            float openNodeF = openNode.getF();
            float openNodeG = openNode.getG();
            Vector2 openNodePos = openNode.getPosition();
            ///Debug.Log("compare new node to current node: pos = " + openNodePos + ", F = " + openNodeF + ", G = " + openNodeG + ", H = " + openNode.getH());

            //replace smallest node if current node has smaller F
            // or it's a tie and current node has bigger G (farther from start)
            if ((openNodeF < smallestNode.getF()) || ((openNodeF == smallestNode.getF()) && (openNodeG > smallestNode.getG())))
            {
                smallestNode = openNode;
                ///Debug.Log("found a new smallest node: new pos = " + smallestNode.getPosition() + ", new F = " + smallestNode.getF() + ", new G = " + smallestNode.getG()+ ", new H = " + smallestNode.getH());
            }

            //also check if this is the same node as the new one
            if (openNodePos == newNodePos)
            {
                ///Debug.Log("open node has same pos as new node");
                //if this position is already here, don't add it to end of nodelist
                shouldAddNode = false;

                //if existing version has smaller F, don't place this node
                //if position is here with bigger F, replace it
                if (openNodeF > newNodeF)
                {
                    ///Debug.Log("open node has bigger F than new node");
                    nodeList[i] = newNode;
                }

                ///TODO: what if it's the same F but a smaller G? current version keeps the older one
            }
        }
        ///Debug.Log("should we add the new node? " + shouldAddNode);

        //finished checking all nodes   
        //add node to end of nodeList if we didn't replace any spots
        if (shouldAddNode)
        {
            nodeList.Add(newNode);
        }

        printNodeList();
        Debug.Log("final length = " + nodeList.Count + ", final smallest: pos = " + smallestNode.getPosition() + ", F = " + smallestNode.getF() + ", G = " + smallestNode.getG() + ", H = " + smallestNode.getH());

        //return the current smallest node
        return smallestNode;
    }


    //helper function to draw path
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        int pathSize = closedList.Count;

        for (int g = 0; g < pathSize - 1; ++g)
        {
            Vector2 origin = ((A_StarNode)closedList[g]).getPosition();
            Vector2 next = ((A_StarNode)closedList[g + 1]).getPosition();
            Gizmos.DrawLine(origin, next);
        }

        Gizmos.color = Color.yellow;
        Vector2 rayOrigin = (Vector2)transform.position + rayOffset;
        Gizmos.DrawLine(rayOrigin, rayOrigin + Vector2.down * checkDistanceFall);
        Gizmos.DrawLine(rayOrigin, rayOrigin + Vector2.up * checkDistanceJump);
        Gizmos.DrawLine(rayOrigin, rayOrigin + Vector2.left * checkDistanceX);
        Gizmos.DrawLine(rayOrigin, rayOrigin + Vector2.right * checkDistanceX);
    }

    ///TODO: does anything go in FixedUpdate?
    void FixedUpdate()
    {

    }

}
