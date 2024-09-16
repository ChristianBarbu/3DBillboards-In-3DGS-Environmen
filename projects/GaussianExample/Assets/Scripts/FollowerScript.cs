using UnityEngine;

public class FollowerScript : MonoBehaviour
{
    public Transform subject; // The object to follow
    public float factor = 1f; // The movement factor

    private Vector3 lastSubjectPosition;

    private void Start()
    {
        if (subject == null)
        {
            Debug.LogError("Subject not assigned to FollowerScript!");
            enabled = false;
            return;
        }

        lastSubjectPosition = subject.position;
    }

    private void Update()
    {
        Vector3 subjectMovement = subject.position - lastSubjectPosition;
        
        Vector3 followerMovement = subjectMovement * factor;
        transform.position += followerMovement;
        lastSubjectPosition = subject.position;
    }
}