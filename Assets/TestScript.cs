using UnityEngine;

public class NPCMovement : MonoBehaviour
{
    // You can change this speed number directly inside Unity's Inspector later
    public float moveSpeed = 5f;

    void Start()
    {
        // This code runs once right when you press Play
        Debug.Log("The cylinder script has started!");
    }

    void Update()
    {
        // This code runs constantly, every single frame
        // Right now, it will just move the cylinder forward slowly
        transform.Translate(Vector3.forward * moveSpeed * Time.deltaTime);
    }
}
