using System.Collections.Generic;
using System.Linq;
using CrudApp.Models;

namespace CrudApp.Data
{
    public class InMemoryProductRepository : IProductRepository
    {
        private readonly List<Product> _products = new();
        private int _nextId = 1;

        public IEnumerable<Product> GetAll() => _products;

        public Product? GetById(int id) => _products.FirstOrDefault(p => p.Id == id);

        public Product Create(Product product)
        {
            product.Id = _nextId++;
            _products.Add(product);
            return product;
        }

        public Product? Update(int id, Product product)
        {
            var existing = GetById(id);
            if (existing == null)
            {
                return null;
            }

            existing.Name = product.Name;
            existing.Price = product.Price;
            existing.Quantity = product.Quantity;
            return existing;
        }

        public bool Delete(int id)
        {
            var existing = GetById(id);
            if (existing == null)
            {
                return false;
            }

            _products.Remove(existing);
            return true;
        }
    }
}
