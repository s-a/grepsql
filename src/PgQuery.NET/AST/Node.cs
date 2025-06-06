using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.Collections;

namespace PgQuery.NET.AST
{
    public partial class Node
    {
        /// <summary>
        /// Gets all child nodes of this node using Protocol Buffers reflection
        /// </summary>
        public IEnumerable<Node> GetChildren()
        {
            var descriptor = Descriptor;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                var value = field.Accessor.GetValue(this);
                
                if (value == null) continue;

                if (value is Node childNode)
                {
                    yield return childNode;
                }
                else if (value is RepeatedField<Node> nodeList)
                {
                    foreach (var node in nodeList)
                    {
                        yield return node;
                    }
                }
            }
        }
    }
} 