using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    public class InteractionRouteRelay : MonoBehaviour
    {
        [Serializable]
        public class Route
        {
            public string storyId;
            public string optionId;
            public UnityEvent onMatched = new UnityEvent();
        }

        [SerializeField] private InteractionDirector director;
        [SerializeField] private List<Route> routes = new List<Route>();

        private void OnEnable()
        {
            if (director == null)
            {
                director = FindFirstObjectByType<InteractionDirector>();
            }

            if (director != null)
            {
                director.OptionInvoked += HandleOptionInvoked;
            }
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.OptionInvoked -= HandleOptionInvoked;
            }
        }

        private void HandleOptionInvoked(InteractionInvocation invocation)
        {
            if (invocation == null)
            {
                return;
            }

            for (int index = 0; index < routes.Count; index++)
            {
                var route = routes[index];
                if (route == null)
                {
                    continue;
                }

                if (string.Equals(route.storyId, invocation.StoryId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(route.optionId, invocation.OptionId, StringComparison.OrdinalIgnoreCase))
                {
                    route.onMatched?.Invoke();
                }
            }
        }
    }
}
