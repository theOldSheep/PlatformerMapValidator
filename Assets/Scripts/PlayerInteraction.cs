using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    // You can see this in the Inspector to track your score
    public int coinsCollected = 0;

    // This runs when the Player's Collider touches a "Trigger" Collider
    private void OnTriggerEnter2D(Collider2D other)
    {
        // Check if the object we hit has the "Coin" tag
        if (other.CompareTag("Coin"))
        {
            CollectCoin(other.gameObject);
        }
    }

    void CollectCoin(GameObject coin)
    {
        coinsCollected++;
        // Debug.Log("Coins: " + coinsCollected);

        // Hide the coin
        coin.GetComponent<StatefulCoin>().Collect();
    }
}