vec4 AddShadowCustom(vec4 over, vec2 uv, float brightness, float opacity, float base, float scalar, float power)
{
	float gradient = (distance(uv, vec2(0.5))+base)*scalar;
	gradient=pow(gradient, power);
	gradient = clamp(1.0-gradient, 0, 1);
	vec4 fill =vec4(over.rgb, 1);
	return mix(vec4(vec3(brightness),gradient*opacity),fill, over.a);
}

vec4 AddShadow(vec4 over, vec2 uv, float brightness, float opacity)
{
	return AddShadowCustom(over, uv, brightness, opacity, 0.0, 2.0, 2.0);
}