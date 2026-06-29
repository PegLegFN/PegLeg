const float EPSILON = 0.0001f;

vec2 flipbookSlice(vec2 uv, vec2 sliceAmount, float cellCount, float currentIndex, bool repeat, bool pingPong)
{
	float sliceCount = floor(cellCount>0.0 ? cellCount : ((sliceAmount.x*sliceAmount.y)+cellCount));
	float sliceIndex = floor(mod(currentIndex, sliceCount));
	//sliceIndex = 15.0;
	vec2 sliceOffset = vec2(mod(sliceIndex, sliceAmount.x),floor(sliceIndex/sliceAmount.x));

	vec2 slicedUV = (sliceOffset/sliceAmount) + (uv/sliceAmount);
	vec2 slicedUnit = mod(slicedUV,1.0/sliceAmount);
	vec2 slicedGrid = floor(slicedUV*sliceAmount);
	float slicedIndex = (slicedGrid.y*sliceAmount.x)+slicedGrid.x;
	slicedUV = slicedUnit+(sliceOffset/sliceAmount);
	bool isNotRepeating = (slicedIndex-sliceIndex<=EPSILON && slicedIndex-sliceIndex>=-EPSILON);
	return (repeat || isNotRepeating) ? slicedUV : vec2(-1);
}

vec2 flipbookSlice(vec2 uv, vec2 sliceAmount, float cellCount, float currentIndex, bool repeat){
	return flipbookSlice(uv, sliceAmount, cellCount, currentIndex, repeat, false);
}