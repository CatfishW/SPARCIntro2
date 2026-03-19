using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    public sealed class BedroomStorySubtitleTypewriter
    {
        private float revealedCharacters;
        private string fullText = string.Empty;

        public string FullText => fullText;

        public string VisibleText
        {
            get
            {
                if (string.IsNullOrEmpty(fullText))
                {
                    return string.Empty;
                }

                var visibleCount = Mathf.Clamp(Mathf.FloorToInt(revealedCharacters), 0, fullText.Length);
                return fullText.Substring(0, visibleCount);
            }
        }

        public bool IsComplete => string.IsNullOrEmpty(fullText) || revealedCharacters >= fullText.Length;

        public void Begin(string text)
        {
            fullText = text ?? string.Empty;
            revealedCharacters = 0f;
        }

        public void Advance(float deltaTime, float charactersPerSecond)
        {
            if (string.IsNullOrEmpty(fullText) || deltaTime <= 0f)
            {
                return;
            }

            revealedCharacters = Mathf.Min(fullText.Length, revealedCharacters + (Mathf.Max(1f, charactersPerSecond) * deltaTime));
        }

        public void Clear()
        {
            fullText = string.Empty;
            revealedCharacters = 0f;
        }
    }
}
