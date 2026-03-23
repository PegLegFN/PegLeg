using Godot;
using System;

[Tool]
public partial class CardPackTilter : TextureRect
{
    [Export]
    float rotSpeed = 30f;
    [Export]
    float rotRange = 10;
    protected ShaderMaterial shaderMat;
    public override void _Ready()
    {
        MouseEntered += () => isHovered = true;
        MouseExited += () => isHovered = false;
    }
    private void OnRectUpdated()
    {
        SetShaderVector(Size, "ControlSize");
    }

    public override void _Draw()
    {

    }

    bool isHovered;
    Vector3 prevRot;
    Vector3 currentRot;
    Vector3 targetRot;
    Vector2 bgOffset;
    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint())
            SetShaderVector(Size, "ControlSize");
        if (isHovered)
        {
            var mousePos = GetViewport().GetMousePosition();
            var thisRect = GetGlobalRect();
            var scaledPos = (mousePos - thisRect.Position) / thisRect.Size;
            targetRot = new(Mathf.DegToRad(rotRange + (scaledPos.Y * rotRange * -2)), Mathf.DegToRad(rotRange + (scaledPos.X * rotRange * -2)), 0);
            bgOffset = scaledPos * 0.1f;
        }
        else
            targetRot = Vector3.Zero;
        float blend = (float)Mathf.Pow(0.5f, delta * rotSpeed);
        currentRot = targetRot.Lerp(currentRot, blend);
        if ((prevRot - currentRot).Length() < 0.001)
            return;
        prevRot = currentRot;
        var matrix = Transform3D.Identity.Rotated(Vector3.Up, currentRot.Y).Rotated(Vector3.Right, -currentRot.X);
        SetShaderMatrix(matrix, "Transformation");
        SetShaderVector(bgOffset, "BGUVOffset");
    }
    void SetShaderVector(Vector2 val, string property)
    {
        shaderMat ??= Material as ShaderMaterial;
        shaderMat.SetShaderParameter(property, val);
    }
    void SetShaderMatrix(Transform3D val, string property)
    {
        shaderMat ??= Material as ShaderMaterial;
        shaderMat.SetShaderParameter(property, val);
    }
}
