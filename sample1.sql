-- Sample SQL file 1
-- Contains various types of queries

SELECT id, name, email
FROM users
WHERE active = true;

INSERT INTO users (name, email, created_at)
VALUES ('John Doe', 'john@example.com', NOW());

UPDATE users 
SET last_login = NOW()
WHERE id = 123;

SELECT u.name, COUNT(o.id) as order_count
FROM users u
LEFT JOIN orders o ON u.id = o.user_id
GROUP BY u.name
HAVING COUNT(o.id) > 5;

DELETE FROM sessions
WHERE expires_at < NOW();

SELECT *
FROM products
WHERE price BETWEEN 10 AND 100
ORDER BY price DESC
LIMIT 20; 