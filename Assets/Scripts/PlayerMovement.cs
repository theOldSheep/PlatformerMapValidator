using System.Collections.Generic;
using UnityEngine;


public class PlayerMovement : MonoBehaviour, IMovement, IStateComponent
{
    public enum MoveActionType { None, Left, Right, Jump, Dash } //here need to add also the new mechanic....
    private readonly List<MoveAction> candidateActions = new List<MoveAction>(8);

    public void SetUseUnityInput(bool enabled) => useUnityInput = enabled;


    public struct MoveAction
    {
        public MoveActionType type;
        public MoveAction(MoveActionType type) => this.type = type;
        public float simDeltaTime 
        {
            get
            {
                switch(type)
                {
                    case MoveActionType.Dash:
                        return 0.051f;
                    default:
                        return 0.041f;
                }
            }
        }
        public int simIterations 
        {
            get
            {
                switch(type)
                {
                    case MoveActionType.Dash:
                        return 15;
                    case MoveActionType.None:
                        return 6;
                    default:
                        return 6;
                }
            }
        }
        public bool simEarlyTermination 
        {
            get
            {
                switch(type)
                {
                    case MoveActionType.Dash:
                        return false;
                    default:
                        return true;
                }
            }
        }
    }


    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float jumpForce = 10f;

    [Header("Dash Settings")]                  //example of the dash 
    public float dashSpeed = 12f;
    public float dashDuration = 0.25f;
    public float dashCooldown = 0.75f;
    public bool allowAirDash = true;

    [Header("Ground Check (Raycast)")]
    public float groundCheckDistance = 0.1f;
    public LayerMask groundLayer;
    public Vector2 rayOffset = new Vector2(0f, 0f);

    [Header("Control")]
    [Tooltip("If true, reads input in Update(). If false external code must call setInput().")]
    [SerializeField] bool useUnityInput = true;

    private Rigidbody2D rb;
    private bool isGrounded;
    private bool wantJump;
    private float moveInput;
    private bool wantDash;

    private int facing = 1; //facing right
    private float dashCooldownRemaining;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null) Debug.LogError("PlayerMovement requires a Rb2d");
    }


    void Update()
    {
        if (!useUnityInput) return;


        float horizontal = Input.GetAxisRaw("Horizontal");
        bool jumpDown = Input.GetKeyDown(KeyCode.Space);

        bool dashDown = Input.GetKeyDown(KeyCode.LeftShift);

        SetInput(horizontal, jumpDown, dashDown);//here the other mechanics need to be implemented too...
    }


    void FixedUpdate()
    {
        SimulateStep(Time.fixedDeltaTime);
    }

    ///<summary
    /// Single source of truth for movement.
    /// planer will call this on a simulation
    ///</summary>

    public void SimulateStep(float dt)
    {

        isGrounded = IsGrounded();

        rb.velocity = new Vector2(moveInput * moveSpeed, rb.velocity.y);


        if (wantJump)
        {
            Jump();
            wantJump = false;
        }

        DashTick(dt);

    }


    public void SetInput(float horizontal, bool jumpDown, bool dashDown) //here dash will need to be add the dashDown bool...
    {

        moveInput = Mathf.Clamp(horizontal, -1f, 1f);

        if (moveInput < 0f) facing = -1;
        else if (moveInput > 0f) facing = 1;

        if (jumpDown && IsGrounded())
            wantJump = true;

        if (dashDown)
            wantDash = true;

    }


    public void ApplyAction(MoveAction action)
    {
        switch (action.type)
        {
            case MoveActionType.None:
                SetInput(0f, false, false);  //need update this for each new mechanic...
                break;
            case MoveActionType.Left:
                MoveLeft();
                break;
            case MoveActionType.Right:
                MoveRight();
                break;
            case MoveActionType.Jump:
                SetInput(moveInput, true, false);
                break;
            case MoveActionType.Dash:
                SetInput(moveInput, false, true);
                break;
                //here need to be added the new mechanics.

        }

    }


    public virtual IReadOnlyList<MoveAction> GetCandidateActions()
    {
        candidateActions.Clear();

        candidateActions.Add(new MoveAction(MoveActionType.None));
        candidateActions.Add(new MoveAction(MoveActionType.Left));
        candidateActions.Add(new MoveAction(MoveActionType.Right));

        bool groundedNow = IsGrounded();
        if (groundedNow)
            candidateActions.Add(new MoveAction(MoveActionType.Jump));

        bool canDash = dashCooldownRemaining <= 0f && (groundedNow || allowAirDash);
        if (canDash)
            candidateActions.Add(new MoveAction(MoveActionType.Dash));

        return candidateActions;

    }


    // ---------------- IMovement (runtime “capability” methods) ----------------
    public void Jump()
    {
        rb.velocity = new Vector2(rb.velocity.x, 0f);
        rb.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
    }

    public void MoveLeft()
    {
        moveInput = -1f;
        facing = -1;
    }

    public void MoveRight()
    {
        moveInput = 1f;
        facing = 1;
    }

    public void DashTick(float dt)
    {
        if (dashCooldownRemaining > 0f) {
            dashCooldownRemaining = Mathf.Max(0f, dashCooldownRemaining - dt);
        } 

        if (dashCooldownRemaining + dashDuration >= dashCooldown)
        {
            rb.velocity = new Vector2(facing * dashSpeed, rb.velocity.y);
        }

        bool groundedNow = IsGrounded();
        if (!groundedNow && !allowAirDash) return;

        if (wantDash)
        {
            // Dash if cooldown is over
            if (dashCooldownRemaining <= 0f)
            {
                dashCooldownRemaining = dashCooldown;
            }
            // Stop the attempt to dash whatsoever
            wantDash = false;
        }
    }

    public bool IsGrounded()
    {
        Vector2 origin = (Vector2)transform.position + rayOffset;
        RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundCheckDistance, groundLayer);
        isGrounded = hit.collider != null;
        return isGrounded;
    }

    public float GetJumpForce() => jumpForce;
    public float GetMoveSpeed() => moveSpeed;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector2 origin = (Vector2)transform.position + rayOffset;
        Gizmos.DrawLine(origin, origin + Vector2.down * groundCheckDistance);
    }

    private List<IStateFeature> _cachedGameStateFeatures = new List<IStateFeature> {
        // Position
        new StatePosVelFeature<float> {
            Type = PosVelType.PosX
        },
        new StatePosVelFeature<float> {
            Type = PosVelType.PosY
        },
        // Velocity
        new StatePosVelFeature<float> {
            Type = PosVelType.VelX
        },
        new StatePosVelFeature<float> {
            Type = PosVelType.VelY
        },
        // on ground
        new StateFeature<bool> {
            Type = FeatureType.Discrete,
        },
        // facing, move input, want jump/dash, dash cooldown.
        new StateFeature<int> {
            CustomEncoding = (ignored) => 0
        },
        new StateFeature<float> {
            CustomEncoding = (ignored) => 0
        },
        new StateFeature<bool> {
            CustomEncoding = (ignored) => 0
        },
        new StateFeature<bool> {
            CustomEncoding = (ignored) => 0
        },
        new StateFeature<float> {
            CustomEncoding = (cooldown) => {
                if (cooldown <= 0f) return 0; // ready to dash
                return 2;
            }
        }
    };
    public List<IStateFeature> GetFeatures() => _cachedGameStateFeatures;
    public List<object> GetFeaturesRawValue() => new List<object> {
        transform.position.x,
        transform.position.y,
        rb.velocity.x,
        rb.velocity.y,
        isGrounded,
        facing,
        moveInput,
        wantJump,
        wantDash,
        dashCooldownRemaining
    };
    public void RestoreFeaturesRawValue(List<object> rawValues)
    {
        transform.position = new Vector3((float)rawValues[0], (float)rawValues[1], transform.position.z);
        rb.velocity = new Vector2((float)rawValues[2], (float)rawValues[3]);
        isGrounded = (bool)rawValues[4];
        facing = (int)rawValues[5];
        moveInput = (float)rawValues[6];
        wantJump = (bool)rawValues[7];
        wantDash = (bool)rawValues[8];
        dashCooldownRemaining = (float)rawValues[9];
    }
}

