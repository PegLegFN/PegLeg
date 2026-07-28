vec2 scaleVector2(vec2 v, vec2 scale, vec2 pivot)
{
	return ((v-pivot)*scale)+pivot;
}