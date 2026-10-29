import unittest
from unittest.mock import Mock

from delegat_sdk import DelegatClient


class ShopRequestTests(unittest.TestCase):
    def test_shop_request_calls_read_only_agent_route(self):
        client = DelegatClient()
        client._req = Mock(return_value={"ok": True, "status": 200, "body": "{}"})

        result = client.shop_request(17, "seller", "/v3/product/list?limit=10")

        self.assertEqual(result["status"], 200)
        client._req.assert_called_once_with(
            "POST",
            "/marketplace/17/read",
            {"host": "seller", "path": "/v3/product/list?limit=10"},
        )

    def test_shop_request_rejects_urls_and_non_positive_connection_ids(self):
        client = DelegatClient()
        client._req = Mock()

        for connection_id, path in [
            (0, "/products"),
            (-2, "/products"),
            (True, "/products"),
            (1, "https://attacker.example/products"),
            (1, "//attacker.example/products"),
            (1, "/products#fragment"),
        ]:
            with self.subTest(connection_id=connection_id, path=path):
                with self.assertRaises(ValueError):
                    client.shop_request(connection_id, "seller", path)

        client._req.assert_not_called()


class ShopReadTests(unittest.TestCase):
    def test_shop_read_calls_named_read_operation_route(self):
        client = DelegatClient()
        client._req = Mock(return_value={"ok": True, "status": 200, "body": "{}"})

        result = client.shop_read(17, "product_list", {"limit": 25, "cursor": "abc"})

        self.assertEqual(result["status"], 200)
        client._req.assert_called_once_with(
            "POST",
            "/marketplace/17/read-operation",
            {"operation": "product_list", "arguments": {"limit": 25, "cursor": "abc"}},
        )

    def test_shop_read_rejects_bad_ids_operations_and_arguments_locally(self):
        client = DelegatClient()
        client._req = Mock()

        for connection_id, operation, arguments in [
            (0, "product_list", {}),
            (True, "product_list", {}),
            (1, "POST /v3/product/import", {}),
            (1, "Product_List", {}),
            (1, "product_list", []),
            (1, "product_list", {"payload": "x" * 17_000}),
        ]:
            with self.subTest(connection_id=connection_id, operation=operation):
                with self.assertRaises(ValueError):
                    client.shop_read(connection_id, operation, arguments)

        client._req.assert_not_called()

    def test_shop_operations_lists_adapter_read_contract(self):
        client = DelegatClient()
        operations = {"operations": [{"name": "product_cards", "argumentsSchemaJson": "{}"}]}
        client._req = Mock(return_value=operations)

        result = client.shop_operations(17)
        self.assertEqual({}, result["operations"][0]["argumentsSchema"])
        self.assertEqual("{}", result["operations"][0]["argumentsSchemaJson"])
        client._req.assert_called_once_with("GET", "/marketplace/17/read-operations")

    def test_shop_operations_rejects_bad_connection_id(self):
        client = DelegatClient()
        client._req = Mock()

        for connection_id in (0, True, "17"):
            with self.subTest(connection_id=connection_id):
                with self.assertRaises(ValueError):
                    client.shop_operations(connection_id)
        client._req.assert_not_called()


if __name__ == "__main__":
    unittest.main()
