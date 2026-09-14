using SmartX.Shared.Models;

namespace SmartX.Shared.Services;

public static class DeploymentValidator
{
    public static bool ValidateNodeConfiguration(DeploymentNode node)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (node.Parent == null)
        {
            return node.IsActive;
        }

        if (!node.IsActive)
        {
            return false;
        }

        return ValidateNodeConfiguration(node.Parent);
    }

    public static string GetNodePath(DeploymentNode node)
    {
        if (node == null)
        {
            throw new ArgumentNullException(nameof(node));
        }

        if (node.Parent == null)
        {
            return node.Name;
        }

        return GetNodePath(node.Parent) + " > " + node.Name;
    }
}