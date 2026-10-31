namespace UwEditor.Render
{
    /// <summary>
    /// The shaders, written once in the subset GLSL ES 3.00 and GLSL 3.30 share; the version
    /// line is put in front at run time (ANGLE on Windows gives GLES, Linux and macOS desktop GL).
    /// </summary>
    static class Shaders
    {
        public static string Header(bool gles)
        {
            return gles ? "#version 300 es\nprecision highp float;\nprecision highp int;\nprecision highp sampler2DArray;\n" : "#version 330 core\n";
        }

        public const string LevelVertex = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec2 aUv;
layout(location = 2) in float aLayer;
layout(location = 3) in float aShade;
layout(location = 4) in float aTile;
uniform mat4 uViewProj;
uniform vec3 uFadeCentre;
out vec2 vUv;
out float vLayer;
out float vShade;
out float vDist;
flat out int vTile;
void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vUv = aUv;
    vLayer = aLayer;
    vShade = aShade;
    vDist = length(aPos - uFadeCentre);
    vTile = int(aTile + 0.5);
}";

        public const string LevelFragment = @"
in vec2 vUv;
in float vLayer;
in float vShade;
in float vDist;
flat in int vTile;
uniform vec3 uEye;
uniform sampler2DArray uTex;
uniform int uSelTile;
uniform int uHoverTile;
uniform vec4 uShading;
uniform float uLight[16];
uniform float uMono;
// the game's light (3d/VIEW3D.C preset_grid): the colour map of a square d whole squares away
vec3 gameLight(vec3 rgb, float dist)
{
    if (uShading.w < 0.0) return rgb;
    int d = int(floor(dist));
    if (float(d) > uShading.w) return vec3(0.0);
    int u = int(sqrt(2.0 * float(8 * d) * float(8 * d)));
    int t = ((u * int(uShading.x)) >> 6) + int(uShading.y);
    if (t < 0) t = 0;
    int m = min(14, t + int(uShading.z));
    float k = uLight[m];
    if (uMono > 0.5) { float g = dot(rgb, vec3(0.299, 0.587, 0.114)); rgb = vec3(g); }
    return rgb * k;
}
out vec4 fragColor;
void main()
{
    // a ceiling (negative shade) shows only from below, as in the game (3d/GRIDDB.C: eye below 0x3F4)
    if (vShade < 0.0 && uEye.y > 3.98) discard;
    vec4 c = texture(uTex, vec3(vUv, vLayer));
    vec3 rgb = c.rgb * abs(vShade);
    rgb = gameLight(rgb, vDist);
    if (vTile == uSelTile) rgb = mix(rgb, vec3(1.0, 0.82, 0.25), 0.35);
    else if (vTile == uHoverTile) rgb = rgb * 1.25 + vec3(0.04);
    fragColor = vec4(rgb, 1.0);
}";

        public const string SpriteVertex = @"
layout(location = 0) in vec3 aBase;
layout(location = 1) in vec2 aCorner;
layout(location = 2) in vec2 aUv;
layout(location = 3) in float aObject;
layout(location = 4) in float aAlpha;
uniform mat4 uViewProj;
uniform vec3 uRight;
uniform vec3 uUp;
uniform vec3 uFadeCentre;
out vec2 vUv;
out float vAlpha;
out float vDist;
flat out int vObject;
void main()
{
    vec3 p = aBase + uRight * aCorner.x + uUp * aCorner.y;   // always facing the camera
    gl_Position = uViewProj * vec4(p, 1.0);
    vUv = aUv;
    vAlpha = aAlpha;
    vDist = length(p - uFadeCentre);
    vObject = int(aObject + 0.5);
}";

        public const string SpriteFragment = @"
in vec2 vUv;
in float vAlpha;
in float vDist;
flat in int vObject;
uniform sampler2D uAtlas;
uniform int uSelObject;
uniform int uHoverObject;
uniform vec4 uShading;
uniform float uLight[16];
uniform float uMono;
// the game's light (3d/VIEW3D.C preset_grid): the colour map of a square d whole squares away
vec3 gameLight(vec3 rgb, float dist)
{
    if (uShading.w < 0.0) return rgb;
    int d = int(floor(dist));
    if (float(d) > uShading.w) return vec3(0.0);
    int u = int(sqrt(2.0 * float(8 * d) * float(8 * d)));
    int t = ((u * int(uShading.x)) >> 6) + int(uShading.y);
    if (t < 0) t = 0;
    int m = min(14, t + int(uShading.z));
    float k = uLight[m];
    if (uMono > 0.5) { float g = dot(rgb, vec3(0.299, 0.587, 0.114)); rgb = vec3(g); }
    return rgb * k;
}
out vec4 fragColor;
void main()
{
    vec4 c = texture(uAtlas, vUv);
    if (c.a < 0.5) discard;
    vec3 rgb = c.rgb;
    rgb = gameLight(rgb, vDist);
    if (vObject == uSelObject) rgb = mix(rgb, vec3(1.0, 0.85, 0.2), 0.5) + vec3(0.1);
    else if (vObject == uHoverObject) rgb = rgb * 1.3 + vec3(0.06);
    fragColor = vec4(rgb, vAlpha);
}";

        public const string ModelVertex = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec2 aUv;
layout(location = 2) in float aLayer;
layout(location = 3) in vec3 aColor;
layout(location = 4) in float aObject;
uniform mat4 uViewProj;
uniform vec3 uFadeCentre;
out vec2 vUv;
out float vLayer;
out vec3 vColor;
out float vDist;
flat out int vObject;
void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vUv = aUv;
    vLayer = aLayer;
    vColor = aColor;
    vDist = length(aPos - uFadeCentre);
    vObject = int(aObject + 0.5);
}";

        public const string ModelFragment = @"
in vec2 vUv;
in float vLayer;
in vec3 vColor;
in float vDist;
flat in int vObject;
uniform sampler2DArray uTex;
uniform int uSelObject;
uniform int uHoverObject;
uniform vec4 uShading;
uniform float uLight[16];
uniform float uMono;
// the game's light (3d/VIEW3D.C preset_grid): the colour map of a square d whole squares away
vec3 gameLight(vec3 rgb, float dist)
{
    if (uShading.w < 0.0) return rgb;
    int d = int(floor(dist));
    if (float(d) > uShading.w) return vec3(0.0);
    int u = int(sqrt(2.0 * float(8 * d) * float(8 * d)));
    int t = ((u * int(uShading.x)) >> 6) + int(uShading.y);
    if (t < 0) t = 0;
    int m = min(14, t + int(uShading.z));
    float k = uLight[m];
    if (uMono > 0.5) { float g = dot(rgb, vec3(0.299, 0.587, 0.114)); rgb = vec3(g); }
    return rgb * k;
}
out vec4 fragColor;
void main()
{
    vec4 c = vLayer < 0.0 ? vec4(1.0) : texture(uTex, vec3(clamp(vUv, 0.0, 1.0), vLayer));
    if (c.a < 0.5) discard;
    vec3 rgb = c.rgb * vColor;
    rgb = gameLight(rgb, vDist);
    if (vObject == uSelObject) rgb = mix(rgb, vec3(1.0, 0.85, 0.2), 0.5) + vec3(0.1);
    else if (vObject == uHoverObject) rgb = rgb * 1.3 + vec3(0.06);
    fragColor = vec4(rgb, 1.0);
}";

        public const string LineVertex = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aColor;
uniform mat4 uViewProj;
out vec4 vColor;
void main()
{
    gl_Position = uViewProj * vec4(aPos, 1.0);
    vColor = aColor;
}";

        public const string LineFragment = @"
in vec4 vColor;
out vec4 fragColor;
void main() { fragColor = vColor; }";
    }
}
