float evalDF(float val, float threshold, float smoothness)
{
	return smoothstep(threshold, threshold+smoothness, val);
}
vec3 evalDF(vec3 val, float threshold, float smoothness)
{
	return vec3(
		evalDF(val.r, threshold, smoothness),
		evalDF(val.g, threshold, smoothness),
		evalDF(val.b, threshold, smoothness)
	);
}
float median(vec3 rgb)
{
	return max(min(rgb.r,rgb.g), min(max(rgb.r,rgb.g),rgb.b));
}
float evalMSDF(vec3 val, float threshold, float smoothness)
{
	return evalDF(median(val), threshold, smoothness);
}
float blendMSDF(vec3 from, vec3 to, float time, float threshold, float smoothness)
{
	return evalDF(mix(median(from), median(to), time), threshold, smoothness);
}