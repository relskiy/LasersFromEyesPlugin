namespace EyeLasers.Configs
{
    public struct LaserOffset
    {
        public float Height;
        public float Forward;
        public float Width;

        public LaserOffset(float height, float forward = 0.125f, float width = 0.0335f)
        {
            Height = height;
            Forward = forward;
            Width = width;
        }
    }
}