// fxc /T ps_2_0 /E main /O3 /Fo meal-lens.ps meal-lens.hlsl
// Center and radius are relative to the square desktop sample.
sampler2D desktop : register(s0);
float2 center : register(c0);
float2 bend : register(c1); // inverse radius, signed displacement

float4 main(float2 uv : TEXCOORD0) : COLOR0
{
    float2 offset = uv - center;
    float2 local = offset * bend.x;
    float falloff = saturate(1 - dot(local, local));
    float2 sampleAt = uv - offset * (bend.y * falloff * falloff);
    float2 edge = min(uv, 1 - uv);
    float alpha = saturate(abs(bend.y) * 13) * saturate(falloff * 3)
                * saturate(min(edge.x, edge.y) * 40);
    return tex2D(desktop, sampleAt) * alpha;
}
