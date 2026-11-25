using UnityEngine;

namespace TankCollision
{
    /// <summary>
    /// Запускает модель автоматически при нажатии Play в любой сцене (ничего настраивать вручную не нужно).
    /// Если объект с TankCollisionSimulation уже есть в сцене — используется он.
    /// </summary>
    static class SimulationBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (Object.FindAnyObjectByType<TankCollisionSimulation>() != null) return;
            new GameObject("Модель: неупругий удар").AddComponent<TankCollisionSimulation>();
        }
    }
}
