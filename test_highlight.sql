-- Test SQL file for highlighting
SELECT id, name, email 
FROM users 
WHERE status = 'active' 
  AND created_at > '2023-01-01';

INSERT INTO orders (user_id, product_id, quantity)
VALUES (1, 100, 2);

UPDATE products 
SET price = 29.99 
WHERE category = 'electronics';

DELETE FROM logs 
WHERE created_at < NOW() - INTERVAL '30 days';

-- Complex query with subqueries
SELECT u.name, 
       COUNT(o.id) as order_count,
       AVG(o.total) as avg_order_value
FROM users u
LEFT JOIN orders o ON u.id = o.user_id
WHERE u.status = 'active'
GROUP BY u.id, u.name
HAVING COUNT(o.id) > 5; 