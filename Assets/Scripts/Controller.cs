using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Controller : MonoBehaviour
{
    [SerializeField] private Collider2D currentStandingPlatform;
    private Collider2D entityCollider;

    // Start is called before the first frame update
    private void Start()
    {
        entityCollider = GetComponent<Collider2D>();
    }

    public void dropFromPlatform() {
        if (currentStandingPlatform != null)
            StartCoroutine(disableCollision(0.35f));
    }

    private void OnCollisionEnter2D(Collision2D collision) {
        if (collision.gameObject.layer == 8) {
            currentStandingPlatform = collision.collider;
        }
        
    }

    private void OnCollisionExit2D(Collision2D collision) {
        if (collision.gameObject.layer == 8) {
            currentStandingPlatform = null;
        }
    }

    private IEnumerator disableCollision(float time) {
        var platform = currentStandingPlatform;
        Physics2D.IgnoreCollision(entityCollider, platform, true);
        yield return new WaitForSeconds(time);
        Physics2D.IgnoreCollision(entityCollider, platform, false);
    }
}
