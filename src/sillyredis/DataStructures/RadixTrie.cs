class RadixNode
{
    public Dictionary<char, RadixNode> Children { get; set; }
    public bool IsEnd { get; set; }
    public RadixNode()
    {
        Children = new Dictionary<char, RadixNode>();
        IsEnd = false;
    }
}

public class RadixTrie
{
    private RadixNode root;

    public RadixTrie()
    {
        root = new RadixNode();
    }

    public void insert(string Key)
    {
        var RadixCurrent = root;
        foreach(char c in Key)
        {
            if (!RadixCurrent.Children.ContainsKey(c))
            {
                RadixCurrent.Children[c] = new RadixNode();
            }

            RadixCurrent = RadixCurrent.Children[c];
        }

        RadixCurrent.IsEnd = true;
    }

    public int FindCommonCharacter(string Key, string current)
    {
        int len = Math.Min(Key.Length, current.Length);
        int i = 0;
        while(i<len && Key[i] == current[i]) i++;

        return i;
    }
}

