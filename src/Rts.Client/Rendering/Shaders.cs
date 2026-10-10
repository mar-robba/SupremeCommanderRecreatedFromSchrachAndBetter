using System.Numerics;
using Raylib_cs;

namespace Rts.Client.Rendering;

/// <summary>Shared GLSL 330 shaders.</summary>
public static unsafe class Shaders
{
    private const string LitVs = """
        #version 330
        in vec3 vertexPosition;
        in vec3 vertexNormal;
        uniform mat4 mvp;
        uniform mat4 matNormal;
        out vec3 fragNormal;
        void main() {
            fragNormal = normalize(vec3(matNormal * vec4(vertexNormal, 0.0)));
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string LitFs = """
        #version 330
        in vec3 fragNormal;
        uniform vec4 colDiffuse;
        uniform vec3 sunDir;
        out vec4 finalColor;
        void main() {
            vec3 n = normalize(fragNormal);
            float d = max(dot(n, sunDir), 0.0);
            float sky = 0.5 + 0.5 * n.y;
            vec3 c = colDiffuse.rgb * (0.30 + 0.25 * sky + 0.65 * d);
            finalColor = vec4(c, colDiffuse.a);
        }
        """;

    private static Shader? _lit;

    /// <summary>Simple directional-light shader using the material diffuse color (no textures).</summary>
    public static Shader LoadLit()
    {
        if (_lit is Shader s) return s;
        var sh = Raylib.LoadShaderFromMemory(LitVs, LitFs);
        sh.Locs[(int)ShaderLocationIndex.MatrixMvp] = Raylib.GetShaderLocation(sh, "mvp");
        sh.Locs[(int)ShaderLocationIndex.MatrixNormal] = Raylib.GetShaderLocation(sh, "matNormal");
        sh.Locs[(int)ShaderLocationIndex.ColorDiffuse] = Raylib.GetShaderLocation(sh, "colDiffuse");
        Raylib.SetShaderValue(sh, Raylib.GetShaderLocation(sh, "sunDir"), TerrainRenderer.SunDir, ShaderUniformDataType.Vec3);
        _lit = sh;
        return sh;
    }
}
