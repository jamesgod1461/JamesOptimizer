using System;
using System.Windows;

namespace JamesOptimizer
{
    public enum CalloutPosition
    {
        Auto,
        Top,
        Bottom,
        Left,
        Right
    }

    public class WalkthroughStep
    {
        /// <summary>
        /// The target UI element to highlight.
        /// </summary>
        public FrameworkElement? TargetElement { get; set; }

        /// <summary>
        /// Title for the callout.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Description text for the callout.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Desired position of the callout relative to the target element.
        /// </summary>
        public CalloutPosition CalloutPosition { get; set; } = CalloutPosition.Auto;

        /// <summary>
        /// Optional hook that runs before the overlay measures the target element. 
        /// Useful for expanding hidden tabs or flyouts.
        /// </summary>
        public Action? OnStepEnter { get; set; }

        /// <summary>
        /// Optional hook that runs after the step advances.
        /// Useful for closing flyouts or cleaning up state.
        /// </summary>
        public Action? OnStepExit { get; set; }
    }
}
