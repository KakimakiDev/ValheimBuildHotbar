using UnityEngine;
using UnityEngine.UI;

namespace Kakimaki.BuildHotbar;

// A real annular sector: no rectangular backing or geometry in the centre hole.
public sealed class RadialSlice : Image
{
    public int Slot;
    public const float InnerRadius = 116f;
    public const float OuterRadius = 268f;
    private const float HalfAngle = 22.5f;
    private const int Segments = 24;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float start = Slot * 45f - HalfAngle;
        float end = Slot * 45f + HalfAngle;
        Band(mesh, InnerRadius, OuterRadius, start, end, 3f, color);
    }

    private static void Band(VertexHelper mesh, float inner, float outer, float start, float end, float halfGap, Color tint)
    {
        int first = mesh.currentVertCount;
        for (int i = 0; i <= Segments; i++)
        {
            // Offset each radial boundary by a fixed perpendicular distance.
            // Different angles at the inner/outer arc keep the side edges parallel.
            float innerInset = Mathf.Asin(halfGap / inner) * Mathf.Rad2Deg;
            float outerInset = Mathf.Asin(halfGap / outer) * Mathf.Rad2Deg;
            float innerAngle = Mathf.Lerp(start + innerInset, end - innerInset, i / (float)Segments) * Mathf.Deg2Rad;
            float outerAngle = Mathf.Lerp(start + outerInset, end - outerInset, i / (float)Segments) * Mathf.Deg2Rad;
            mesh.AddVert(new Vector2(Mathf.Sin(innerAngle), Mathf.Cos(innerAngle)) * inner, tint, Vector2.zero);
            mesh.AddVert(new Vector2(Mathf.Sin(outerAngle), Mathf.Cos(outerAngle)) * outer, tint, Vector2.zero);
            if (i == 0) continue;
            int index = first + i * 2;
            mesh.AddTriangle(index - 2, index - 1, index);
            mesh.AddTriangle(index - 1, index + 1, index);
        }
    }
}
