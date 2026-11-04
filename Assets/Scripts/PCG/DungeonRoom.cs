using UnityEngine;

[System.Serializable]
public class DungeonRoom
{
    public int x;
    public int y;
    public int width;
    public int height;

    public DungeonRoom(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public Vector2Int Center
    {
        get
        {
            return new Vector2Int(
                x + width / 2,
                y + height / 2
            );
        }
    }

    public bool Contains(Vector2Int p)
    {
        return p.x >= x && p.x < x + width &&
               p.y >= y && p.y < y + height;
    }
}
