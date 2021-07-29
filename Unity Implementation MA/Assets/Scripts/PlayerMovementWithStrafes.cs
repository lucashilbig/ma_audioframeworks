using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Class for play movement with csgo style strafe movement.
/// This is a modiefied version of Alpharoa's (Credits to him): https://www.youtube.com/watch?v=AVjbCn5i_rk
/// </summary>
public class PlayerMovementWithStrafes : MonoBehaviour
{
	public CharacterController controller;
	public Transform GroundCheck;
	public LayerMask GroundMask;
	public AudioSource footsteps;

	private float wishspeed2;
	private float gravity = -20f;
	float wishspeed;

	public float GroundDistance = 0.4f;
	public float moveSpeed = 7.0f;  // Ground move speed
	public float runAcceleration = 14f;   // Ground accel
	public float runDeacceleration = 10f;   // Deacceleration that occurs when running on the ground
	public float airAcceleration = 2.0f;  // Air accel
	public float airDeacceleration = 2.0f;    // Deacceleration experienced when opposite strafing
	public float airControl = 0.3f;  // How precise air control is
	public float sideStrafeAcceleration = 50f;   // How fast acceleration occurs to get up to sideStrafeSpeed when side strafing
	public float sideStrafeSpeed = 1f;    // What the max speed to generate when side strafing
	public float jumpSpeed = 8.0f;
	public float friction = 6f;
	private float playerTopVelocity = 0;
	public float playerFriction = 0f;
	float addspeed;
	float accelspeed;
	float currentspeed;
	float zspeed;
	float speed;
	float dot;
	float k;
	float accel;
	float newspeed;
	float control;
	float drop;

	bool JumpQueue = false;
	bool wishJump = false;

	//UI
	public Text posText;
	public Text velText; 
	public Text speedText;
	private Vector3 lastPos;
	private Vector3 moved;
	private Vector3 PlayerVel;
	private float ModulasSpeed;
	//End UI

	private Vector3 playerVelocity;
	Vector3 wishdir;
	Vector3 vec;

	bool IsGrounded;

	public Transform player;
	Vector3 udp;


    private void Start()
    {
		// Set audio footstep settings
		if(footsteps != null)
		{
			footsteps.spatialBlend = 1.0f;
			footsteps.loop = true;
		}

        // This is for UI
		lastPos = player.position;
	}

    // Update is called once per frame
    void Update()
	{
		#region //UI
		moved = player.position - lastPos;
		lastPos = player.position;
		PlayerVel = moved / Time.fixedDeltaTime;
		ModulasSpeed = Mathf.Sqrt(PlayerVel.z * PlayerVel.z + PlayerVel.x * PlayerVel.x);

		posText.text = string.Format("Position: {0}  ,  {1}  ,  {2}", lastPos.x.ToString("0.00"), lastPos.y.ToString("0.00"), lastPos.z.ToString("0.00"));
		velText.text = string.Format("Velocity: {0}  ,  {1}  ,  {2}", Mathf.Abs(PlayerVel.x).ToString("0.00"), Mathf.Abs(PlayerVel.y).ToString("0.00"), Mathf.Abs(PlayerVel.z).ToString("0.00"));
		speedText.text = string.Format("Speed: {0}", ModulasSpeed.ToString("0.00"));

		#endregion

		IsGrounded = Physics.CheckSphere(GroundCheck.position, GroundDistance, GroundMask);

		QueueJump();

		/* Movement, here's the important part */
		if (controller.isGrounded)
			GroundMove();
		else if (!controller.isGrounded)
			AirMove();

		// Move the controller
		controller.Move(playerVelocity * Time.deltaTime);

		// Calculate top velocity
		udp = playerVelocity;
		udp.y = 0;
		if (udp.magnitude > playerTopVelocity)
			playerTopVelocity = udp.magnitude;
	}

	//Queues the next jump
	void QueueJump()
	{
		if (Input.GetButtonDown("Jump") && IsGrounded)
		{
			wishJump = true;
		}

		if (!IsGrounded && Input.GetButtonDown("Jump"))
		{
			JumpQueue = true;
		}
		if (IsGrounded && JumpQueue)
		{
			wishJump = true;
			JumpQueue = false;
		}
	}

	//Calculates wish acceleration
	public void Accelerate(Vector3 wishdir, float wishspeed, float accel)
	{
		currentspeed = Vector3.Dot(playerVelocity, wishdir);
		addspeed = wishspeed - currentspeed;
		if (addspeed <= 0)
			return;
		accelspeed = accel * Time.deltaTime * wishspeed;
		if (accelspeed > addspeed)
			accelspeed = addspeed;

		playerVelocity.x += accelspeed * wishdir.x;
		playerVelocity.z += accelspeed * wishdir.z;
	}

	//Execs when the player is in the air
	public void AirMove()
	{
		wishdir = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
		wishdir = transform.TransformDirection(wishdir);

		wishspeed = wishdir.magnitude;

		wishspeed *= 7f;

		wishdir.Normalize();

		// Aircontrol
		wishspeed2 = wishspeed;
		if (Vector3.Dot(playerVelocity, wishdir) < 0)
			accel = airDeacceleration;
		else
			accel = airAcceleration;

		// If the player is ONLY strafing left or right
		if (Input.GetAxis("Horizontal") == 0 && Input.GetAxis("Vertical") != 0)
		{
			if (wishspeed > sideStrafeSpeed)
				wishspeed = sideStrafeSpeed;
			accel = sideStrafeAcceleration;
		}

		Accelerate(wishdir, wishspeed, accel);

		AirControl(wishdir, wishspeed2);

		// !Aircontrol

		// Apply gravity
		playerVelocity.y += gravity * Time.deltaTime;

		//Stop Audio (footssteps) while in the air
		if(footsteps != null && footsteps.isPlaying)
		{
			footsteps.Pause();
		}

		/**
			* Air control occurs when the player is in the air, it allows
			* players to move side to side much faster rather than being
			* 'sluggish' when it comes to cornering.
			*/

		void AirControl(Vector3 wishdir, float wishspeed)
		{
			// Can't control movement if not moving forward or backward
			if (Input.GetAxis("Horizontal") == 0 || wishspeed == 0)
				return;

			zspeed = playerVelocity.y;
			playerVelocity.y = 0;
			/* Next two lines are equivalent to idTech's VectorNormalize() */
			speed = playerVelocity.magnitude;
			playerVelocity.Normalize();

			dot = Vector3.Dot(playerVelocity, wishdir);
			k = 32;
			k *= airControl * dot * dot * Time.deltaTime;

			// Change direction while slowing down
			if (dot > 0)
			{
				playerVelocity.x = playerVelocity.x * speed + wishdir.x * k;
				playerVelocity.y = playerVelocity.y * speed + wishdir.y * k;
				playerVelocity.z = playerVelocity.z * speed + wishdir.z * k;

				playerVelocity.Normalize();
			}

			playerVelocity.x *= speed;
			playerVelocity.y = zspeed; // Note this line
			playerVelocity.z *= speed;

		}
	}
	/**
		* Called every frame when the engine detects that the player is on the ground
		*/
	public void GroundMove()
	{
		// Do not apply friction if the player is queueing up the next jump
		if (!wishJump)
			ApplyFriction(1.0f);
		else
			ApplyFriction(0);
		
		wishdir = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));
		wishdir = transform.TransformDirection(wishdir);
		wishdir.Normalize();

		wishspeed = wishdir.magnitude;
		wishspeed *= moveSpeed;

		Accelerate(wishdir, wishspeed, runAcceleration);

		// Reset the gravity velocity
		playerVelocity.y = 0;

		if (wishJump)
		{
			playerVelocity.y = jumpSpeed;
			wishJump = false;
		}

		// Play audio footsteps if velocity is high enough
		if(playerVelocity.sqrMagnitude > 0.1f)
		{
			if(footsteps != null && !footsteps.isPlaying)
			{
				footsteps.Play();
			}
		}

		/**
			* Applies friction to the player, called in both the air and on the ground
			*/
		void ApplyFriction(float t)
		{
			vec = playerVelocity; // Equivalent to: VectorCopy();
			vec.y = 0f;
			speed = vec.magnitude;
			drop = 0f;

			/* Only if the player is on the ground then apply friction */
			if (controller.isGrounded)
			{
				control = speed < runDeacceleration ? runDeacceleration : speed;
				drop = control * friction * Time.deltaTime * t;
			}

			newspeed = speed - drop;
			playerFriction = newspeed;
			if (newspeed < 0)
				newspeed = 0;
			if (speed > 0)
				newspeed /= speed;

			playerVelocity.x *= newspeed;
			// playerVelocity.y *= newspeed;
			playerVelocity.z *= newspeed;
		}
	}
}
