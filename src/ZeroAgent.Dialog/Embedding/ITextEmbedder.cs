using System;

namespace ZeroAgent.Dialog.Embedding
{
    /// <summary>
    /// Represents a universal text embedding contract for semantic, lexical, and hybrid vector generation.
    /// </summary>
    public interface ITextEmbedder
    {
        /// <summary>
        /// Gets the vector dimension of the embedding space.
        /// </summary>
        int Dimension { get; }

        /// <summary>
        /// Generates a dense, L2-normalized vector embedding from the input text.
        /// </summary>
        float[] Embed(string text);

        /// <summary>
        /// Generates a dense, L2-normalized vector embedding directly into the destination span without heap allocations.
        /// </summary>
        void Embed(string text, Span<float> destination);
    }
}
