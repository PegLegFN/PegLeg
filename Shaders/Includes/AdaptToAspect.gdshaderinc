vec2 adaptToAspect(vec2 controlSize, vec2 shiftDirection, vec2 textureResolution, vec2 uv, bool fit)
{
	float horizontalRatio = textureResolution.x/controlSize.x;
	float verticalRatio = textureResolution.y/controlSize.y;
	float ratio = fit ? max(horizontalRatio,verticalRatio) : min(horizontalRatio,verticalRatio);
	vec2 scaledResolution = textureResolution/ratio;

	//vec2 scaledShift = clamp(shiftDirection, vec2(0),vec2(1)) * (controlSize-scaledResolution);
	vec2 totalShift = (controlSize-scaledResolution)*shiftDirection;
	vec2 shiftedControlSize = (uv*controlSize)-totalShift;
	vec2 result = shiftedControlSize/scaledResolution;

	return result;
	//return (scaledUV-scaledShift)/scaledResolution;
}
vec2 adaptToAspect(vec2 controlSize, vec2 shiftDirection, sampler2D tex, vec2 uv, bool fit)
{
	return adaptToAspect(controlSize, shiftDirection, vec2(textureSize(tex, 0)), uv, fit);
}

vec2 GetFitSize(vec2 controlSize, vec2 textureResolution)
{
	float horizontalRatio = textureResolution.x/controlSize.x;
	float verticalRatio = textureResolution.y/controlSize.y;
	float ratio = max(horizontalRatio,verticalRatio);
	return textureResolution/ratio;
}