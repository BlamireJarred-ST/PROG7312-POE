using SmartX.Shared.Models;

namespace SmartX.Shared.Services;

// Validation methods for deployment nodes
public static class DeploymentValidator
{
    // Validates the configuration of a deployment node and its ancestors
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

    // Returns the full path of a deployment node in the hierarchy
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