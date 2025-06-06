-- Sample SQL file 2
-- More complex queries

WITH active_users AS (
    SELECT * FROM users WHERE status = 'active'
),
recent_orders AS (
    SELECT * FROM orders WHERE created_at > '2024-01-01'
)
SELECT u.name, COUNT(o.id) 
FROM active_users u 
LEFT JOIN recent_orders o ON u.id = o.user_id
GROUP BY u.name;

SELECT 
    CASE 
        WHEN age < 18 THEN 'minor'
        WHEN age < 65 THEN 'adult'
        ELSE 'senior'
    END as age_group,
    COUNT(*) as count
FROM users
GROUP BY age_group;

SELECT *
FROM orders
WHERE total > (
    SELECT AVG(total) FROM orders WHERE status = 'completed'
);

UPDATE inventory
SET quantity = quantity - 1
WHERE product_id IN (
    SELECT product_id 
    FROM order_items 
    WHERE order_id = 12345
); 