using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("基础设置（解决角色变大问题）")]
    [Tooltip("角色初始缩放X值（需和胶囊体初始Scale.x一致，默认1）")]
    public float initialScaleX = 1f;

    [Header("移动设置")]
    public float moveSpeed = 5f;
    public float wallSlideSpeed = 1.2f;

    [Header("跳跃设置")]
    public float jumpForce = 7f;
    public float secondJumpForce = 6f;
    [Range(1, 5)] public int maxJumpCount = 2;
    public float groundCheckDistance = 0.3f;
    public LayerMask groundLayer;
    public int groundCheckPoints = 3;
    public float coyoteTime = 0.1f;
    public float jumpInputCoolDown = 0.05f;

    [Header("空洞风格贴墙跳跃设置（已修复）")]
    public LayerMask wallLayer;
    public float wallCheckDistance = 0.25f;
    public Vector2 wallCheckOffset = new Vector2(0.2f, 0.3f);
    public float wallDistanceThreshold = 0.2f;
    public float wallBufferTime = 0.25f;
    public float wallJumpHorizontal = 6f; // 优化：默认值提升至6f，增强远离墙壁的推力
    public float wallJumpForce = 8.5f;
    public float wallJumpXDecay = 0.85f; // 优化：衰减系数提升至0.85f，延长水平惯性

    [Header("钩锁设置")]
    public GameObject ropePrefab;
    public float maxRopeLength = 12f;
    public Vector2 ropeOriginOffset = new Vector2(0, 0.5f);
    public float dragSpeed = 15f;
    public float dragAcceleration = 2f;
    public LayerMask ropeAttachLayers;
    public float ropeDetectionRadius = 0.4f;

    // 状态变量
    private Rigidbody2D rb;
    private bool isGrounded;
    private bool isTouchingWall;
    private bool isLeftWall;
    private bool isRightWall;
    private float horizontalInput;
    private List<Vector2> groundCheckPositions = new List<Vector2>();
    private int currentJumpCount;
    private bool jumpInput;
    private float coyoteTimer;
    private float jumpInputTimer;
    private float wallBufferTimer;
    private bool isWallJumping;

    // 钩锁变量
    private GameObject currentRope;
    private LineRenderer ropeLineRenderer;
    private Vector2 ropeHookPoint;
    private bool isRopeAttached;
    private bool isDragging;
    private float currentDragSpeed;

    void Start()
    {
        // 1. 初始化刚体（确保重力和碰撞正常）
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 3.5f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.mass = 1f;
        }
        currentJumpCount = 0;
        isRopeAttached = false;

        // 2. 记录初始缩放（解决角色变大问题）
        if (Mathf.Abs(transform.localScale.x) > 0)
        {
            initialScaleX = Mathf.Abs(transform.localScale.x);
        }
        transform.localScale = new Vector3(initialScaleX, transform.localScale.y, transform.localScale.z);
    }

    void Update()
    {
        horizontalInput = Input.GetAxisRaw("Horizontal");
        CalculateGroundCheckPositions();
        CheckGrounded();
        CheckWallTouch();
        jumpInputTimer -= Time.deltaTime;

        // 调试日志：查看关键状态
        Debug.Log($"=== 调试状态 ===" +
                  $"\n是否贴墙：{isTouchingWall}（左墙：{isLeftWall}，右墙：{isRightWall}）" +
                  $"\n缓冲时间：{wallBufferTimer:F2}s" +
                  $"\n可墙跳：{!isRopeAttached && (isTouchingWall || wallBufferTimer > 0)}" +
                  $"\n角色缩放X：{transform.localScale.x:F2}（初始：{initialScaleX:F2}）");

        // 跳跃条件判定
        bool canWallJump = !isRopeAttached && (isTouchingWall || wallBufferTimer > 0);
        bool canNormalJump = !isRopeAttached && !canWallJump && (currentJumpCount < maxJumpCount || coyoteTimer > 0);

        if (Input.GetButtonDown("Jump") && jumpInputTimer <= 0)
        {
            if (canWallJump)
            {
                Debug.Log("触发墙跳！");
                jumpInput = true;
                jumpInputTimer = jumpInputCoolDown;
                isWallJumping = true;
            }
            else if (canNormalJump)
            {
                Debug.Log("触发普通跳跃");
                jumpInput = true;
                jumpInputTimer = jumpInputCoolDown;
                isWallJumping = false;
            }
            else
            {
                Debug.Log("无法跳跃：条件不满足");
            }
        }

        HandleRopeSkill();
    }

    private void FixedUpdate()
    {
        Move();
        if (jumpInput)
        {
            Jump();
            jumpInput = false;
        }
        // 墙跳后衰减水平速度
        if (isWallJumping && Mathf.Abs(rb.velocity.x) > 0)
        {
            rb.velocity = new Vector2(rb.velocity.x * wallJumpXDecay, rb.velocity.y);
            if (Mathf.Abs(rb.velocity.x) < 1f)
                isWallJumping = false;
        }
    }

    #region 核心逻辑（已修复墙跳冲突）
    void CalculateGroundCheckPositions()
    {
        groundCheckPositions.Clear();
        Collider2D playerCollider = GetComponent<Collider2D>();

        if (playerCollider == null)
        {
            groundCheckPositions.Add(new Vector2(transform.position.x, transform.position.y - 0.6f));
            return;
        }

        Bounds colliderBounds = playerCollider.bounds;
        float bottomY = colliderBounds.min.y - 0.1f;
        float halfWidth = colliderBounds.extents.x * 0.9f;

        int actualCheckPoints = Mathf.Max(groundCheckPoints, 3);
        float startX = transform.position.x - halfWidth;
        float endX = transform.position.x + halfWidth;
        float step = (endX - startX) / (actualCheckPoints - 1);

        for (int i = 0; i < actualCheckPoints; i++)
        {
            float currentX = startX + (step * i);
            groundCheckPositions.Add(new Vector2(currentX, bottomY));
        }
    }

    void Move()
    {
        if (isDragging)
        {
            UpdatePlayerFacing();
            return;
        }

        // 修复1：墙跳时取消向墙吸附速度，避免抵消墙跳推力
        if (isTouchingWall && !isGrounded && !isWallJumping)
        {
            float xVelocity = 0; // 关键修改：移除向墙的强制X速度（原±0.5f）
            float yVelocity = Mathf.Min(rb.velocity.y, wallSlideSpeed);
            rb.velocity = new Vector2(xVelocity, yVelocity);
            Debug.Log("处于贴墙下滑状态，无强制X吸附速度");
        }
        else if (isGrounded)
        {
            rb.velocity = new Vector2(horizontalInput * moveSpeed, rb.velocity.y);
        }
        else
        {
            float airControl = isWallJumping ? 0.4f : 0.6f;
            rb.velocity = new Vector2(horizontalInput * moveSpeed * airControl, rb.velocity.y);
        }

        UpdatePlayerFacing();
    }

    void Jump()
    {
        rb.velocity = new Vector2(rb.velocity.x, 0);

        if (isTouchingWall || wallBufferTimer > 0)
        {
            float horizontalDir = isLeftWall ? 1 : -1;
            rb.velocity = new Vector2(horizontalDir * wallJumpHorizontal, wallJumpForce);
            // 修复2：删除冗余的缓冲时间设置，避免逻辑混乱（原wallBufferTimer = wallBufferTime * 0.7f;）
            Debug.Log($"墙跳参数：水平方向{horizontalDir}，推力{wallJumpHorizontal}，垂直力{wallJumpForce}");
        }
        else
        {
            float jumpStrength = currentJumpCount == 0 ? jumpForce : secondJumpForce;
            rb.velocity = new Vector2(rb.velocity.x, jumpStrength);
            currentJumpCount = Mathf.Min(currentJumpCount + 1, maxJumpCount);
        }
    }

    void CheckGrounded()
    {
        bool wasGrounded = isGrounded;
        isGrounded = false;

        foreach (Vector2 checkPos in groundCheckPositions)
        {
            RaycastHit2D hit = Physics2D.Raycast(checkPos, Vector2.down, groundCheckDistance, groundLayer);
            if (hit.collider != null)
            {
                isGrounded = true;
                break;
            }
        }

        if (!wasGrounded && isGrounded)
        {
            currentJumpCount = 0;
            coyoteTimer = coyoteTime;
            Debug.Log("落地：重置跳跃次数");
        }
        else if (!isGrounded)
        {
            coyoteTimer -= Time.deltaTime;
        }
    }

    void CheckWallTouch()
    {
        isLeftWall = false;
        isRightWall = false;

        // 获取忽略射线图层（默认索引2）
        int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreLayer == -1) ignoreLayer = 2;

        // 保存原图层并临时切换
        int originalLayer = gameObject.layer;
        gameObject.layer = ignoreLayer;

        // 左墙检测
        RaycastHit2D leftHit = Physics2D.BoxCast(
            (Vector2)transform.position - wallCheckOffset,
            new Vector2(0.1f, 1.2f),
            0, Vector2.left, wallCheckDistance, wallLayer
        );
        isLeftWall = leftHit && leftHit.distance <= wallDistanceThreshold;

        // 右墙检测
        RaycastHit2D rightHit = Physics2D.BoxCast(
            (Vector2)transform.position + wallCheckOffset,
            new Vector2(0.1f, 1.2f),
            0, Vector2.right, wallCheckDistance, wallLayer
        );
        isRightWall = rightHit && rightHit.distance <= wallDistanceThreshold;

        // 恢复原图层
        gameObject.layer = originalLayer;

        // 更新贴墙状态和缓冲时间
        isTouchingWall = isLeftWall || isRightWall;
        if (isTouchingWall)
            wallBufferTimer = wallBufferTime;
        else if (wallBufferTimer > 0)
            wallBufferTimer -= Time.deltaTime;
    }

    void UpdatePlayerFacing()
    {
        if (horizontalInput != 0)
        {
            transform.localScale = new Vector3(
                Mathf.Sign(horizontalInput) * initialScaleX,
                transform.localScale.y,
                transform.localScale.z
            );
        }
        else if (isTouchingWall)
        {
            transform.localScale = new Vector3(
                isLeftWall ? initialScaleX : -initialScaleX,
                transform.localScale.y,
                transform.localScale.z
            );
        }
    }

    private void OnCollisionEnter2D(Collision2D other)
    {
        if (((1 << other.gameObject.layer) & groundLayer) != 0)
        {
            foreach (var contact in other.contacts)
            {
                if (contact.normal.y > 0.7f)
                {
                    currentJumpCount = 0;
                    coyoteTimer = coyoteTime;
                }
            }
        }
    }
    #endregion

    #region 钩锁逻辑（保留原功能）
    void HandleRopeSkill()
    {
        if (Input.GetMouseButtonDown(1) && !isRopeAttached && ropePrefab != null)
        {
            FireRope();
            currentDragSpeed = 0;
        }
        else if (Input.GetMouseButton(1) && isRopeAttached)
        {
            isDragging = true;
            DragPlayerToHook();
            UpdateRopeVisual();
        }
        else if (Input.GetMouseButtonUp(1) && isRopeAttached)
        {
            RetractRope();
        }
    }

    void FireRope()
    {
        Vector2 ropeOrigin = (Vector2)transform.position + ropeOriginOffset;
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0;
        Vector2 fireDir = (mouseWorldPos - (Vector3)ropeOrigin).normalized;

        RaycastHit2D hit = Physics2D.CircleCast(
            ropeOrigin, ropeDetectionRadius, fireDir, maxRopeLength, ropeAttachLayers
        );

        if (hit)
        {
            ropeHookPoint = hit.point;
            float length = Vector2.Distance(ropeOrigin, ropeHookPoint);
            if (length > maxRopeLength)
                ropeHookPoint = ropeOrigin + fireDir * maxRopeLength;

            CreateRopeVisual(ropeOrigin, ropeHookPoint);
            isRopeAttached = true;
            isDragging = false;
        }
    }

    void DragPlayerToHook()
    {
        Vector2 toHook = (ropeHookPoint - (Vector2)transform.position).normalized;
        float distance = Vector2.Distance(transform.position, ropeHookPoint);

        currentDragSpeed = Mathf.Lerp(currentDragSpeed, dragSpeed, dragAcceleration * Time.fixedDeltaTime);

        if (distance < 0.5f || distance > maxRopeLength + 0.5f)
        {
            RetractRope();
            return;
        }

        rb.velocity = toHook * currentDragSpeed + new Vector2(horizontalInput * moveSpeed * 0.3f, 0);
    }

    void CreateRopeVisual(Vector2 origin, Vector2 hook)
    {
        if (currentRope != null) Destroy(currentRope);
        currentRope = Instantiate(ropePrefab, origin, Quaternion.identity);
        ropeLineRenderer = currentRope.GetComponent<LineRenderer>();
        if (ropeLineRenderer != null)
        {
            ropeLineRenderer.positionCount = 2;
            ropeLineRenderer.SetPosition(0, origin);
            ropeLineRenderer.SetPosition(1, hook);
        }
    }

    void UpdateRopeVisual()
    {
        if (ropeLineRenderer == null) return;
        ropeLineRenderer.SetPosition(0, (Vector2)transform.position + ropeOriginOffset);
        ropeLineRenderer.SetPosition(1, ropeHookPoint);
    }

    void RetractRope()
    {
        isRopeAttached = false;
        isDragging = false;
        currentDragSpeed = 0;
        if (currentRope != null) Destroy(currentRope);
        currentRope = null;
        ropeLineRenderer = null;
    }
    #endregion

    #region Gizmo调试（保留原功能）
    private void OnDrawGizmos()
    {
        // 地面检测点
        Gizmos.color = Color.green;
        CalculateGroundCheckPositions();
        foreach (var pos in groundCheckPositions)
            Gizmos.DrawLine(pos, pos + Vector2.down * groundCheckDistance);

        // 墙壁检测范围（红色=右墙，蓝色=左墙）
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(
            (Vector2)transform.position - wallCheckOffset,
            new Vector2(wallCheckDistance + 0.1f, 1.2f)
        );
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(
            (Vector2)transform.position + wallCheckOffset,
            new Vector2(wallCheckDistance + 0.1f, 1.2f)
        );

        // 钩锁范围
        Vector2 ropeOrigin = (Vector2)transform.position + ropeOriginOffset;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(ropeOrigin, maxRopeLength);
        if (isRopeAttached)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(ropeOrigin, ropeHookPoint);
        }
    }
    #endregion
}