
vec2 PackUV(vec2 uv)
{
	vec2 packUV=(uv-vec2(0.336,0.54))*7.5;
	packUV.y+=packUV.x*-0.05;
	packUV.y*=1.0-(packUV.x*0.1);
	return packUV;
}

vec3 PackCol()
{
	return vec3(0.2890625,0.2578125,0.2265625);
}

vec4 MixPackIcon(vec4 bg, vec2 uv, sampler2D packIcon)
{
	vec4 packMask=texture(packIcon, PackUV(uv));
	return mix(bg,vec4(PackCol(), bg.a),packMask.r*packMask.a);
}