//src: sixrobin
vec2 rotateVector2(vec2 v, float theta, vec2 pivot)
{
    float c = cos(radians(theta));
    float s = sin(radians(theta));
    return vec2(c * (v.x - pivot.x) + s * (v.y - pivot.y) + pivot.x, c * (v.y - pivot.y) - s * (v.x - pivot.x) + pivot.y);
}