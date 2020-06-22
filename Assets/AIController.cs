using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class AIController : MonoBehaviour
{
    [SerializeField] private Transform target;

	[SerializeField] private float movementSpeed = 2.0f;
	[SerializeField] private float rotationSpeed = 0.5f;

	private NavMeshAgent agent;

	private void Awake()
	{
		agent = GetComponent<NavMeshAgent>();
	}

	private void Update()
	{
		agent.SetDestination(target.position);
		if (agent.remainingDistance > agent.stoppingDistance)
		{
			transform.position += transform.forward * movementSpeed * Time.deltaTime;
		}

		var dir = (agent.steeringTarget - transform.position).normalized;
		if (dir != Vector3.zero)
			transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), rotationSpeed);
	}
}
