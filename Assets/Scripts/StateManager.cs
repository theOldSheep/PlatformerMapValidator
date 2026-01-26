public class StateManager : MonoBehaviour
{
    private List<IStateComponent> _stateProviders;
    public float GlobalGranularity = 0.2f;

    void Start()
    {
        // Find all objects implementing the interface and sort by ID
        _stateProviders = FindObjectsOfType<MonoBehaviour>()
            .OfType<IStateComponent>()
            .OrderBy(x => x.ID)
            .ToList();
    }

    public GameStateSnapshot CaptureState()
    {
        List<int> flatState = new List<int>();

        foreach (var provider in _stateProviders)
        {
            foreach (var feature in provider.GetFeatures())
            {
                if (feature.Type == FeatureType.Discrete)
                {
                    flatState.Add(Mathf.RoundToInt(feature.Value));
                }
                else
                {
                    // Continuous: Apply the binning logic
                    int binned = Mathf.FloorToInt(feature.Value / feature.Granularity);
                    flatState.Add(binned);
                }
            }
        }

        return new GameStateSnapshot(flatState.ToArray());
    }
}